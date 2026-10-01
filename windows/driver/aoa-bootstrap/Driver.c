#include <ntddk.h>
#include <wdf.h>
#include <wdfusb.h>
#include <initguid.h>

#include "Public.h"

#define AOA_GET_PROTOCOL 51u
#define AOA_SEND_STRING 52u
#define AOA_START_ACCESSORY 53u
#define AOA_CONTROL_TIMEOUT_MS 750u

typedef struct _DEVICE_CONTEXT {
    WDFUSBDEVICE UsbDevice;
} DEVICE_CONTEXT, *PDEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(
    DEVICE_CONTEXT,
    NatsxGetDeviceContext);

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD NatsxEvtDeviceAdd;
EVT_WDF_DEVICE_PREPARE_HARDWARE NatsxEvtDevicePrepareHardware;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL NatsxEvtIoDeviceControl;

static NTSTATUS
NatsxSendVendorControl(
    _In_ WDFUSBDEVICE UsbDevice,
    _In_ WDF_USB_BMREQUEST_DIRECTION Direction,
    _In_ UCHAR Request,
    _In_ USHORT Index,
    _Inout_updates_bytes_opt_(BufferLength) PVOID Buffer,
    _In_ ULONG BufferLength,
    _Out_opt_ PULONG BytesTransferred
    );

static NTSTATUS
NatsxStartAccessoryMode(
    _In_ WDFDEVICE Device,
    _Out_ PUSHORT ProtocolVersion
    );

static VOID
NatsxForwardRequest(
    _In_ WDFQUEUE Queue,
    _In_ WDFREQUEST Request
    )
{
    WDFDEVICE device = WdfIoQueueGetDevice(Queue);
    WDFIOTARGET target = WdfDeviceGetIoTarget(device);

    WdfRequestFormatRequestUsingCurrentType(Request);

    if (!WdfRequestSend(
            Request,
            target,
            WDF_NO_SEND_OPTIONS)) {
        NTSTATUS status = WdfRequestGetStatus(Request);
        WdfRequestComplete(Request, status);
    }
}

NTSTATUS
DriverEntry(
    _In_ PDRIVER_OBJECT DriverObject,
    _In_ PUNICODE_STRING RegistryPath
    )
{
    WDF_DRIVER_CONFIG config;

    WDF_DRIVER_CONFIG_INIT(
        &config,
        NatsxEvtDeviceAdd);

    return WdfDriverCreate(
        DriverObject,
        RegistryPath,
        WDF_NO_OBJECT_ATTRIBUTES,
        &config,
        WDF_NO_HANDLE);
}

NTSTATUS
NatsxEvtDeviceAdd(
    _In_ WDFDRIVER Driver,
    _Inout_ PWDFDEVICE_INIT DeviceInit
    )
{
    UNREFERENCED_PARAMETER(Driver);

    WDFDEVICE device;
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_PNPPOWER_EVENT_CALLBACKS pnpCallbacks;
    WDF_IO_QUEUE_CONFIG queueConfig;
    NTSTATUS status;

    // This is a pass-through lower-filter prototype. It must never replace
    // the OEM MTP/PTP function driver.
    WdfFdoInitSetFilter(DeviceInit);

    WDF_PNPPOWER_EVENT_CALLBACKS_INIT(
        &pnpCallbacks);
    pnpCallbacks.EvtDevicePrepareHardware =
        NatsxEvtDevicePrepareHardware;
    WdfDeviceInitSetPnpPowerEventCallbacks(
        DeviceInit,
        &pnpCallbacks);

    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(
        &attributes,
        DEVICE_CONTEXT);

    status = WdfDeviceCreate(
        &DeviceInit,
        &attributes,
        &device);

    if (!NT_SUCCESS(status)) {
        return status;
    }

    NatsxGetDeviceContext(device)->UsbDevice =
        WDF_NO_HANDLE;

    status = WdfDeviceCreateDeviceInterface(
        device,
        &GUID_DEVINTERFACE_NATSX_AOA_BOOTSTRAP,
        NULL);

    if (!NT_SUCCESS(status)) {
        return status;
    }

    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(
        &queueConfig,
        WdfIoQueueDispatchParallel);

    queueConfig.EvtIoDeviceControl =
        NatsxEvtIoDeviceControl;

    return WdfIoQueueCreate(
        device,
        &queueConfig,
        WDF_NO_OBJECT_ATTRIBUTES,
        WDF_NO_HANDLE);
}

NTSTATUS
NatsxEvtDevicePrepareHardware(
    _In_ WDFDEVICE Device,
    _In_ WDFCMRESLIST ResourcesRaw,
    _In_ WDFCMRESLIST ResourcesTranslated
    )
{
    UNREFERENCED_PARAMETER(ResourcesRaw);
    UNREFERENCED_PARAMETER(ResourcesTranslated);

    PDEVICE_CONTEXT context =
        NatsxGetDeviceContext(Device);

    if (context->UsbDevice != WDF_NO_HANDLE) {
        return STATUS_SUCCESS;
    }

    WDF_USB_DEVICE_CREATE_CONFIG usbConfig;

    WDF_USB_DEVICE_CREATE_CONFIG_INIT(
        &usbConfig,
        USBD_CLIENT_CONTRACT_VERSION_602);

    NTSTATUS status =
        WdfUsbTargetDeviceCreateWithParameters(
            Device,
            &usbConfig,
            WDF_NO_OBJECT_ATTRIBUTES,
            &context->UsbDevice);

    if (!NT_SUCCESS(status)) {
        // A filter must not prevent the OEM function stack from starting.
        // The NATSX user-mode client will observe DEVICE_NOT_READY instead.
        context->UsbDevice = WDF_NO_HANDLE;
        return STATUS_SUCCESS;
    }

    return STATUS_SUCCESS;
}

VOID
NatsxEvtIoDeviceControl(
    _In_ WDFQUEUE Queue,
    _In_ WDFREQUEST Request,
    _In_ size_t OutputBufferLength,
    _In_ size_t InputBufferLength,
    _In_ ULONG IoControlCode
    )
{
    UNREFERENCED_PARAMETER(InputBufferLength);

    if (IoControlCode == IOCTL_NATSX_AOA_GET_VERSION) {
        if (OutputBufferLength <
            sizeof(NATSX_AOA_VERSION_RESPONSE)) {
            WdfRequestComplete(
                Request,
                STATUS_BUFFER_TOO_SMALL);
            return;
        }

        PNATSX_AOA_VERSION_RESPONSE response = NULL;
        NTSTATUS status =
            WdfRequestRetrieveOutputBuffer(
                Request,
                sizeof(NATSX_AOA_VERSION_RESPONSE),
                (PVOID*)&response,
                NULL);

        if (!NT_SUCCESS(status)) {
            WdfRequestComplete(
                Request,
                status);
            return;
        }

        response->ProtocolVersion =
            NATSX_AOA_BOOTSTRAP_PROTOCOL_VERSION;
        response->DriverBuild =
            NATSX_AOA_BOOTSTRAP_DRIVER_BUILD;

        WdfRequestCompleteWithInformation(
            Request,
            STATUS_SUCCESS,
            sizeof(*response));
        return;
    }

    if (IoControlCode == IOCTL_NATSX_AOA_START) {
        if (OutputBufferLength <
            sizeof(NATSX_AOA_START_RESPONSE)) {
            WdfRequestComplete(
                Request,
                STATUS_BUFFER_TOO_SMALL);
            return;
        }

        PNATSX_AOA_START_RESPONSE response = NULL;
        NTSTATUS status =
            WdfRequestRetrieveOutputBuffer(
                Request,
                sizeof(NATSX_AOA_START_RESPONSE),
                (PVOID*)&response,
                NULL);

        if (!NT_SUCCESS(status)) {
            WdfRequestComplete(
                Request,
                status);
            return;
        }

        USHORT aoaVersion = 0;
        status =
            NatsxStartAccessoryMode(
                WdfIoQueueGetDevice(Queue),
                &aoaVersion);

        if (!NT_SUCCESS(status)) {
            WdfRequestComplete(
                Request,
                status);
            return;
        }

        response->AoaProtocolVersion =
            aoaVersion;
        response->Reserved = 0;

        WdfRequestCompleteWithInformation(
            Request,
            STATUS_SUCCESS,
            sizeof(*response));
        return;
    }

    NatsxForwardRequest(
        Queue,
        Request);
}

static NTSTATUS
NatsxStartAccessoryMode(
    _In_ WDFDEVICE Device,
    _Out_ PUSHORT ProtocolVersion
    )
{
    static const UCHAR manufacturer[] = "NATSX";
    static const UCHAR model[] =
        "NATSX Controller Windows Receiver";
    static const UCHAR description[] =
        "Low-latency NATSX Controller USB transport";
    static const UCHAR version[] = "1";
    static const UCHAR uri[] = "https://natsx.my.id";
    static const UCHAR serial[] = "natsx-controller";

    PDEVICE_CONTEXT context =
        NatsxGetDeviceContext(Device);

    if (context->UsbDevice == WDF_NO_HANDLE) {
        return STATUS_DEVICE_NOT_READY;
    }

    USHORT protocolVersion = 0;
    ULONG bytesTransferred = 0;

    NTSTATUS status =
        NatsxSendVendorControl(
            context->UsbDevice,
            BmRequestDeviceToHost,
            AOA_GET_PROTOCOL,
            0,
            &protocolVersion,
            sizeof(protocolVersion),
            &bytesTransferred);

    if (!NT_SUCCESS(status)) {
        return status;
    }

    if (bytesTransferred != sizeof(protocolVersion) ||
        protocolVersion == 0) {
        return STATUS_NOT_SUPPORTED;
    }

    struct {
        USHORT Index;
        PVOID Buffer;
        ULONG Length;
    } strings[] = {
        { 0, (PVOID)manufacturer, sizeof(manufacturer) },
        { 1, (PVOID)model, sizeof(model) },
        { 2, (PVOID)description, sizeof(description) },
        { 3, (PVOID)version, sizeof(version) },
        { 4, (PVOID)uri, sizeof(uri) },
        { 5, (PVOID)serial, sizeof(serial) },
    };

    for (ULONG i = 0;
         i < RTL_NUMBER_OF(strings);
         ++i) {
        status =
            NatsxSendVendorControl(
                context->UsbDevice,
                BmRequestHostToDevice,
                AOA_SEND_STRING,
                strings[i].Index,
                strings[i].Buffer,
                strings[i].Length,
                NULL);

        if (!NT_SUCCESS(status)) {
            return status;
        }
    }

    status =
        NatsxSendVendorControl(
            context->UsbDevice,
            BmRequestHostToDevice,
            AOA_START_ACCESSORY,
            0,
            NULL,
            0,
            NULL);

    if (!NT_SUCCESS(status)) {
        return status;
    }

    *ProtocolVersion =
        protocolVersion;

    return STATUS_SUCCESS;
}

static NTSTATUS
NatsxSendVendorControl(
    _In_ WDFUSBDEVICE UsbDevice,
    _In_ WDF_USB_BMREQUEST_DIRECTION Direction,
    _In_ UCHAR Request,
    _In_ USHORT Index,
    _Inout_updates_bytes_opt_(BufferLength) PVOID Buffer,
    _In_ ULONG BufferLength,
    _Out_opt_ PULONG BytesTransferred
    )
{
    WDF_USB_CONTROL_SETUP_PACKET setupPacket;
    WDF_REQUEST_SEND_OPTIONS sendOptions;
    WDF_MEMORY_DESCRIPTOR memoryDescriptor;
    PWDF_MEMORY_DESCRIPTOR memoryDescriptorPointer = NULL;
    ULONG localBytesTransferred = 0;

    WDF_USB_CONTROL_SETUP_PACKET_INIT_VENDOR(
        &setupPacket,
        Direction,
        BmRequestToDevice,
        Request,
        0,
        Index);

    WDF_REQUEST_SEND_OPTIONS_INIT(
        &sendOptions,
        WDF_REQUEST_SEND_OPTION_TIMEOUT);

    WDF_REQUEST_SEND_OPTIONS_SET_TIMEOUT(
        &sendOptions,
        WDF_REL_TIMEOUT_IN_MS(
            AOA_CONTROL_TIMEOUT_MS));

    if (BufferLength > 0) {
        if (Buffer == NULL) {
            return STATUS_INVALID_PARAMETER;
        }

        WDF_MEMORY_DESCRIPTOR_INIT_BUFFER(
            &memoryDescriptor,
            Buffer,
            BufferLength);

        memoryDescriptorPointer =
            &memoryDescriptor;
    }

    NTSTATUS status =
        WdfUsbTargetDeviceSendControlTransferSynchronously(
            UsbDevice,
            WDF_NO_HANDLE,
            &sendOptions,
            &setupPacket,
            memoryDescriptorPointer,
            &localBytesTransferred);

    if (BytesTransferred != NULL) {
        *BytesTransferred =
            localBytesTransferred;
    }

    return status;
}
