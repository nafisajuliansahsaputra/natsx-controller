#include <ntddk.h>
#include <wdf.h>
#include <usb.h>
#include <usbdi.h>
#include <usbioctl.h>
#include <usbdlib.h>
#include <wdfusb.h>
#include <initguid.h>

#include "Public.h"

#define AOA_GET_PROTOCOL 51u
#define AOA_SEND_STRING 52u
#define AOA_START_ACCESSORY 53u
#define AOA_CONTROL_TIMEOUT_MS 750u

#define NATSX_CONTROL_DEVICE_NAME L"\\Device\\NatsxAoaBootstrap"
#define NATSX_CONTROL_SYMBOLIC_LINK L"\\DosDevices\\NatsxAoaBootstrap"

typedef struct _DEVICE_CONTEXT {
    WDFUSBDEVICE UsbDevice;
    NTSTATUS LastUsbTargetCreateStatus;
    ULONG UsbTargetCreateAttemptCount;
} DEVICE_CONTEXT, *PDEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(
    DEVICE_CONTEXT,
    NatsxGetDeviceContext);

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD NatsxEvtDeviceAdd;
EVT_WDF_DEVICE_PREPARE_HARDWARE NatsxEvtDevicePrepareHardware;
EVT_WDF_OBJECT_CONTEXT_CLEANUP NatsxEvtDeviceContextCleanup;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL NatsxEvtControlIoDeviceControl;

static WDFCOLLECTION NatsxTargetDevices = NULL;
static WDFWAITLOCK NatsxTargetDevicesLock = NULL;
static WDFDEVICE NatsxControlDevice = NULL;

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

    USHORT protocolVersion = 0;
    ULONG bytesTransferred = 0;
    ULONG usbStatus = 0;

    NTSTATUS status =
        NatsxSendVendorControlRawUrb(
            Device,
            BmRequestDeviceToHost,
            AOA_GET_PROTOCOL,
            0,
            &protocolVersion,
            sizeof(protocolVersion),
            &usbStatus,
            &bytesTransferred);

    if (!NT_SUCCESS(status) ||
        !USBD_SUCCESS((USBD_STATUS)usbStatus)) {
        return NT_SUCCESS(status)
            ? STATUS_IO_DEVICE_ERROR
            : status;
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
        usbStatus = 0;

        status =
            NatsxSendVendorControlRawUrb(
                Device,
                BmRequestHostToDevice,
                AOA_SEND_STRING,
                strings[i].Index,
                strings[i].Buffer,
                strings[i].Length,
                &usbStatus,
                NULL);

        if (!NT_SUCCESS(status) ||
            !USBD_SUCCESS((USBD_STATUS)usbStatus)) {
            return NT_SUCCESS(status)
                ? STATUS_IO_DEVICE_ERROR
                : status;
        }
    }

    usbStatus = 0;

    status =
        NatsxSendVendorControlRawUrb(
            Device,
            BmRequestHostToDevice,
            AOA_START_ACCESSORY,
            0,
            NULL,
            0,
            &usbStatus,
            NULL);

    if (!NT_SUCCESS(status) ||
        !USBD_SUCCESS((USBD_STATUS)usbStatus)) {
        return NT_SUCCESS(status)
            ? STATUS_IO_DEVICE_ERROR
            : status;
    }

    *ProtocolVersion =
        protocolVersion;

    return STATUS_SUCCESS;
}

static NTSTATUS
NatsxSendVendorControlRawUrb(
    _In_ WDFDEVICE Device,
    _In_ WDF_USB_BMREQUEST_DIRECTION Direction,
    _In_ UCHAR Request,
    _In_ USHORT Index,
    _Inout_updates_bytes_opt_(BufferLength) PVOID Buffer,
    _In_ ULONG BufferLength,
    _Out_opt_ PULONG UsbStatus,
    _Out_opt_ PULONG BytesTransferred
    )
{
    if (BufferLength > 0 &&
        Buffer == NULL) {
        return STATUS_INVALID_PARAMETER;
    }

    URB urb;
    RtlZeroMemory(
        &urb,
        sizeof(urb));

    ULONG transferFlags =
        USBD_SHORT_TRANSFER_OK;

    if (Direction == BmRequestDeviceToHost) {
        transferFlags |=
            USBD_TRANSFER_DIRECTION_IN;
    }

    UsbBuildVendorRequest(
        &urb,
        URB_FUNCTION_VENDOR_DEVICE,
        sizeof(struct _URB_CONTROL_VENDOR_OR_CLASS_REQUEST),
        transferFlags,
        0,
        Request,
        0,
        Index,
        Buffer,
        NULL,
        BufferLength,
        NULL);

    WDF_MEMORY_DESCRIPTOR urbDescriptor;
    WDF_MEMORY_DESCRIPTOR_INIT_BUFFER(
        &urbDescriptor,
        &urb,
        sizeof(urb));

    WDF_REQUEST_SEND_OPTIONS sendOptions;
    WDF_REQUEST_SEND_OPTIONS_INIT(
        &sendOptions,
        WDF_REQUEST_SEND_OPTION_TIMEOUT);

    WDF_REQUEST_SEND_OPTIONS_SET_TIMEOUT(
        &sendOptions,
        WDF_REL_TIMEOUT_IN_MS(
            AOA_CONTROL_TIMEOUT_MS));

    PDEVICE_OBJECT physicalDevice =
        WdfDeviceWdmGetPhysicalDevice(
            Device);

    if (physicalDevice == NULL) {
        return STATUS_DEVICE_NOT_READY;
    }

    WDFIOTARGET physicalTarget =
        WDF_NO_HANDLE;

    NTSTATUS status =
        WdfIoTargetCreate(
            Device,
            WDF_NO_OBJECT_ATTRIBUTES,
            &physicalTarget);

    if (!NT_SUCCESS(status)) {
        return status;
    }

    WDF_IO_TARGET_OPEN_PARAMS openParams;
    WDF_IO_TARGET_OPEN_PARAMS_INIT_EXISTING_DEVICE(
        &openParams,
        physicalDevice);

    status =
        WdfIoTargetOpen(
            physicalTarget,
            &openParams);

    if (NT_SUCCESS(status)) {
        status =
            WdfIoTargetSendInternalIoctlOthersSynchronously(
                physicalTarget,
                WDF_NO_HANDLE,
                IOCTL_INTERNAL_USB_SUBMIT_URB,
                &urbDescriptor,
                NULL,
                NULL,
                &sendOptions,
                NULL);

        WdfIoTargetClose(
            physicalTarget);
    }

    if (UsbStatus != NULL) {
        *UsbStatus =
            (ULONG)urb.UrbHeader.Status;
    }

    if (BytesTransferred != NULL) {
        *BytesTransferred =
            urb.UrbControlVendorClassRequest.TransferBufferLength;
    }

    WdfObjectDelete(
        physicalTarget);

    return status;
}
