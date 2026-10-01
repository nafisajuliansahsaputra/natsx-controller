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

static NTSTATUS
NatsxEnsureControlDevice(
    _In_ WDFDRIVER Driver
    );

static VOID
NatsxDeleteControlDeviceIfUnused(
    VOID
    );

static NTSTATUS
NatsxReferenceReadyTarget(
    _Out_ WDFDEVICE* Device
    );

static NTSTATUS
NatsxReferenceSingleAttachedTarget(
    _Out_ WDFDEVICE* Device
    );

static NTSTATUS
NatsxProbeAoaProtocolRawUrb(
    _In_ WDFDEVICE Device,
    _Out_ PUSHORT AoaProtocolVersion,
    _Out_ PULONG UsbStatus,
    _Out_ PULONG BytesTransferred
    );

static VOID
NatsxGetTargetReadiness(
    _Out_ PULONG AttachedTargetCount,
    _Out_ PULONG ReadyUsbTargetCount
    );

NTSTATUS
DriverEntry(
    _In_ PDRIVER_OBJECT DriverObject,
    _In_ PUNICODE_STRING RegistryPath
    )
{
    WDF_DRIVER_CONFIG config;
    WDF_OBJECT_ATTRIBUTES attributes;
    WDFDRIVER driver = WDF_NO_HANDLE;
    NTSTATUS status;

    WDF_DRIVER_CONFIG_INIT(
        &config,
        NatsxEvtDeviceAdd);

    status =
        WdfDriverCreate(
            DriverObject,
            RegistryPath,
            WDF_NO_OBJECT_ATTRIBUTES,
            &config,
            &driver);

    if (!NT_SUCCESS(status)) {
        return status;
    }

    WDF_OBJECT_ATTRIBUTES_INIT(
        &attributes);
    attributes.ParentObject =
        driver;

    status =
        WdfCollectionCreate(
            &attributes,
            &NatsxTargetDevices);

    if (!NT_SUCCESS(status)) {
        return status;
    }

    WDF_OBJECT_ATTRIBUTES_INIT(
        &attributes);
    attributes.ParentObject =
        driver;

    status =
        WdfWaitLockCreate(
            &attributes,
            &NatsxTargetDevicesLock);

    if (!NT_SUCCESS(status)) {
        return status;
    }

    return STATUS_SUCCESS;
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
    NTSTATUS status;

    // This remains a pass-through PnP filter. It must never replace the
    // normal WPD/MTP function driver.
    WdfFdoInitSetFilter(
        DeviceInit);

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
    attributes.EvtCleanupCallback =
        NatsxEvtDeviceContextCleanup;
    attributes.ExecutionLevel =
        WdfExecutionLevelPassive;

    status =
        WdfDeviceCreate(
            &DeviceInit,
            &attributes,
            &device);

    if (!NT_SUCCESS(status)) {
        return status;
    }

    NatsxGetDeviceContext(device)->UsbDevice =
        WDF_NO_HANDLE;
    NatsxGetDeviceContext(device)->LastUsbTargetCreateStatus =
        STATUS_SUCCESS;
    NatsxGetDeviceContext(device)->UsbTargetCreateAttemptCount =
        0;

    WdfWaitLockAcquire(
        NatsxTargetDevicesLock,
        NULL);

    status =
        WdfCollectionAdd(
            NatsxTargetDevices,
            device);

    WdfWaitLockRelease(
        NatsxTargetDevicesLock);

    if (!NT_SUCCESS(status)) {
        // Once installed, the filter must prefer losing NATSX bootstrap
        // capability over preventing the OEM device stack from starting.
        return STATUS_SUCCESS;
    }

    return STATUS_SUCCESS;
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

    // Keep the sideband control plane available whenever the filter itself
    // is loaded. GET_VERSION is a driver-health probe and must not depend on
    // whether this particular USB stack can expose a KMDF WDFUSBDEVICE.
    NTSTATUS status =
        NatsxEnsureControlDevice(
            WdfDeviceGetDriver(Device));

    if (!NT_SUCCESS(status)) {
        // Sideband diagnostics/bootstrap are optional from the perspective of
        // the OEM stack. Do not break MTP if the control object cannot start.
        return STATUS_SUCCESS;
    }

    if (context->UsbDevice == WDF_NO_HANDLE) {
        WDF_USB_DEVICE_CREATE_CONFIG usbConfig;

        WDF_USB_DEVICE_CREATE_CONFIG_INIT(
            &usbConfig,
            USBD_CLIENT_CONTRACT_VERSION_602);

        context->UsbTargetCreateAttemptCount++;

        status =
            WdfUsbTargetDeviceCreateWithParameters(
                Device,
                &usbConfig,
                WDF_NO_OBJECT_ATTRIBUTES,
                &context->UsbDevice);

        context->LastUsbTargetCreateStatus =
            status;

        if (!NT_SUCCESS(status)) {
            // Preserve the OEM WPD/MTP stack. The sideband remains available
            // for GET_VERSION, while START_AOA will fail safely with
            // STATUS_DEVICE_NOT_READY until a usable USB target exists.
            context->UsbDevice =
                WDF_NO_HANDLE;
            return STATUS_SUCCESS;
        }
    }

    return STATUS_SUCCESS;
}

VOID
NatsxEvtDeviceContextCleanup(
    _In_ WDFOBJECT Object
    )
{
    WDFDEVICE device =
        (WDFDEVICE)Object;

    WdfWaitLockAcquire(
        NatsxTargetDevicesLock,
        NULL);

    ULONG count =
        WdfCollectionGetCount(
            NatsxTargetDevices);

    for (ULONG index = 0;
         index < count;
         ++index) {
        if (WdfCollectionGetItem(
                NatsxTargetDevices,
                index) == Object) {
            WdfCollectionRemoveItem(
                NatsxTargetDevices,
                index);
            break;
        }
    }

    WdfWaitLockRelease(
        NatsxTargetDevicesLock);

    UNREFERENCED_PARAMETER(device);

    NatsxDeleteControlDeviceIfUnused();
}

VOID
NatsxEvtControlIoDeviceControl(
    _In_ WDFQUEUE Queue,
    _In_ WDFREQUEST Request,
    _In_ size_t OutputBufferLength,
    _In_ size_t InputBufferLength,
    _In_ ULONG IoControlCode
    )
{
    UNREFERENCED_PARAMETER(Queue);
    UNREFERENCED_PARAMETER(InputBufferLength);

    if (IoControlCode ==
        IOCTL_NATSX_AOA_GET_VERSION) {
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

    if (IoControlCode ==
        IOCTL_NATSX_AOA_GET_STATUS) {
        if (OutputBufferLength <
            sizeof(NATSX_AOA_STATUS_RESPONSE)) {
            WdfRequestComplete(
                Request,
                STATUS_BUFFER_TOO_SMALL);
            return;
        }

        PNATSX_AOA_STATUS_RESPONSE response = NULL;
        NTSTATUS status =
            WdfRequestRetrieveOutputBuffer(
                Request,
                sizeof(NATSX_AOA_STATUS_RESPONSE),
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

        NatsxGetTargetReadiness(
            &response->AttachedTargetCount,
            &response->ReadyUsbTargetCount);

        response->LastUsbTargetCreateStatus =
            STATUS_SUCCESS;
        response->UsbTargetCreateAttemptCount =
            0;

        WdfWaitLockAcquire(
            NatsxTargetDevicesLock,
            NULL);

        ULONG targetCount =
            WdfCollectionGetCount(
                NatsxTargetDevices);

        if (targetCount == 1) {
            WDFDEVICE target =
                (WDFDEVICE)WdfCollectionGetItem(
                    NatsxTargetDevices,
                    0);

            PDEVICE_CONTEXT context =
                NatsxGetDeviceContext(target);

            response->LastUsbTargetCreateStatus =
                context->LastUsbTargetCreateStatus;
            response->UsbTargetCreateAttemptCount =
                context->UsbTargetCreateAttemptCount;
        }

        WdfWaitLockRelease(
            NatsxTargetDevicesLock);

        WdfRequestCompleteWithInformation(
            Request,
            STATUS_SUCCESS,
            sizeof(*response));
        return;
    }

    if (IoControlCode ==
        IOCTL_NATSX_AOA_PROBE_PROTOCOL_RAW) {
        if (OutputBufferLength <
            sizeof(NATSX_AOA_RAW_PROTOCOL_PROBE_RESPONSE)) {
            WdfRequestComplete(
                Request,
                STATUS_BUFFER_TOO_SMALL);
            return;
        }

        PNATSX_AOA_RAW_PROTOCOL_PROBE_RESPONSE response = NULL;
        NTSTATUS status =
            WdfRequestRetrieveOutputBuffer(
                Request,
                sizeof(NATSX_AOA_RAW_PROTOCOL_PROBE_RESPONSE),
                (PVOID*)&response,
                NULL);

        if (!NT_SUCCESS(status)) {
            WdfRequestComplete(
                Request,
                status);
            return;
        }

        RtlZeroMemory(
            response,
            sizeof(*response));

        response->ProtocolVersion =
            NATSX_AOA_BOOTSTRAP_PROTOCOL_VERSION;
        response->DriverBuild =
            NATSX_AOA_BOOTSTRAP_DRIVER_BUILD;

        WDFDEVICE targetDevice =
            WDF_NO_HANDLE;

        status =
            NatsxReferenceSingleAttachedTarget(
                &targetDevice);

        if (NT_SUCCESS(status)) {
            status =
                NatsxProbeAoaProtocolRawUrb(
                    targetDevice,
                    &response->AoaProtocolVersion,
                    &response->UsbStatus,
                    &response->BytesTransferred);

            WdfObjectDereference(
                targetDevice);
        }

        response->SubmitStatus =
            status;
        response->Reserved =
            0;

        // The diagnostic IOCTL itself succeeds so user mode can always read
        // the exact kernel/USB status without START_AOA side effects.
        WdfRequestCompleteWithInformation(
            Request,
            STATUS_SUCCESS,
            sizeof(*response));
        return;
    }

    if (IoControlCode ==
        IOCTL_NATSX_AOA_START) {
        if (OutputBufferLength <
            sizeof(NATSX_AOA_START_RESPONSE)) {
            WdfRequestComplete(
                Request,
                STATUS_BUFFER_TOO_SMALL);
            return;
        }

        WDFDEVICE targetDevice =
            WDF_NO_HANDLE;

        NTSTATUS status =
            NatsxReferenceReadyTarget(
                &targetDevice);

        if (!NT_SUCCESS(status)) {
            WdfRequestComplete(
                Request,
                status);
            return;
        }

        PNATSX_AOA_START_RESPONSE response = NULL;

        status =
            WdfRequestRetrieveOutputBuffer(
                Request,
                sizeof(NATSX_AOA_START_RESPONSE),
                (PVOID*)&response,
                NULL);

        if (NT_SUCCESS(status)) {
            USHORT aoaVersion = 0;

            status =
                NatsxStartAccessoryMode(
                    targetDevice,
                    &aoaVersion);

            if (NT_SUCCESS(status)) {
                response->AoaProtocolVersion =
                    aoaVersion;
                response->Reserved = 0;
            }
        }

        WdfObjectDereference(
            targetDevice);

        if (!NT_SUCCESS(status)) {
            WdfRequestComplete(
                Request,
                status);
            return;
        }

        WdfRequestCompleteWithInformation(
            Request,
            STATUS_SUCCESS,
            sizeof(*response));
        return;
    }

    WdfRequestComplete(
        Request,
        STATUS_INVALID_DEVICE_REQUEST);
}

static NTSTATUS
NatsxEnsureControlDevice(
    _In_ WDFDRIVER Driver
    )
{
    NTSTATUS status =
        STATUS_SUCCESS;

    WdfWaitLockAcquire(
        NatsxTargetDevicesLock,
        NULL);

    if (NatsxControlDevice != NULL) {
        WdfWaitLockRelease(
            NatsxTargetDevicesLock);
        return STATUS_SUCCESS;
    }

    DECLARE_CONST_UNICODE_STRING(
        controlSecurity,
        L"D:P(A;;GA;;;SY)(A;;GRGWGX;;;BA)(A;;GRGW;;;AU)");

    PWDFDEVICE_INIT controlInit =
        WdfControlDeviceInitAllocate(
            Driver,
            &controlSecurity);

    if (controlInit == NULL) {
        WdfWaitLockRelease(
            NatsxTargetDevicesLock);
        return STATUS_INSUFFICIENT_RESOURCES;
    }

    WdfDeviceInitSetExclusive(
        controlInit,
        FALSE);

    WdfDeviceInitSetIoType(
        controlInit,
        WdfDeviceIoBuffered);

    WdfDeviceInitSetCharacteristics(
        controlInit,
        FILE_DEVICE_SECURE_OPEN,
        FALSE);

    DECLARE_CONST_UNICODE_STRING(
        deviceName,
        NATSX_CONTROL_DEVICE_NAME);

    status =
        WdfDeviceInitAssignName(
            controlInit,
            &deviceName);

    if (!NT_SUCCESS(status)) {
        WdfDeviceInitFree(
            controlInit);
        WdfWaitLockRelease(
            NatsxTargetDevicesLock);
        return status;
    }

    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_OBJECT_ATTRIBUTES_INIT(
        &attributes);
    attributes.ExecutionLevel =
        WdfExecutionLevelPassive;

    WDFDEVICE controlDevice =
        WDF_NO_HANDLE;

    status =
        WdfDeviceCreate(
            &controlInit,
            &attributes,
            &controlDevice);

    if (!NT_SUCCESS(status)) {
        if (controlInit != NULL) {
            WdfDeviceInitFree(
                controlInit);
        }

        WdfWaitLockRelease(
            NatsxTargetDevicesLock);
        return status;
    }

    DECLARE_CONST_UNICODE_STRING(
        symbolicLink,
        NATSX_CONTROL_SYMBOLIC_LINK);

    status =
        WdfDeviceCreateSymbolicLink(
            controlDevice,
            &symbolicLink);

    if (!NT_SUCCESS(status)) {
        WdfObjectDelete(
            controlDevice);
        WdfWaitLockRelease(
            NatsxTargetDevicesLock);
        return status;
    }

    WDF_IO_QUEUE_CONFIG queueConfig;
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(
        &queueConfig,
        WdfIoQueueDispatchSequential);
    queueConfig.EvtIoDeviceControl =
        NatsxEvtControlIoDeviceControl;

    status =
        WdfIoQueueCreate(
            controlDevice,
            &queueConfig,
            WDF_NO_OBJECT_ATTRIBUTES,
            WDF_NO_HANDLE);

    if (!NT_SUCCESS(status)) {
        WdfObjectDelete(
            controlDevice);
        WdfWaitLockRelease(
            NatsxTargetDevicesLock);
        return status;
    }

    WdfControlFinishInitializing(
        controlDevice);

    NatsxControlDevice =
        controlDevice;

    WdfWaitLockRelease(
        NatsxTargetDevicesLock);

    return STATUS_SUCCESS;
}

static VOID
NatsxDeleteControlDeviceIfUnused(
    VOID
    )
{
    WDFDEVICE controlDevice =
        WDF_NO_HANDLE;

    WdfWaitLockAcquire(
        NatsxTargetDevicesLock,
        NULL);

    if (WdfCollectionGetCount(
            NatsxTargetDevices) == 0 &&
        NatsxControlDevice != NULL) {
        controlDevice =
            NatsxControlDevice;
        NatsxControlDevice =
            NULL;
    }

    WdfWaitLockRelease(
        NatsxTargetDevicesLock);

    if (controlDevice != WDF_NO_HANDLE) {
        WdfObjectDelete(
            controlDevice);
    }
}

static VOID
NatsxGetTargetReadiness(
    _Out_ PULONG AttachedTargetCount,
    _Out_ PULONG ReadyUsbTargetCount
    )
{
    ULONG attachedCount = 0;
    ULONG readyCount = 0;

    WdfWaitLockAcquire(
        NatsxTargetDevicesLock,
        NULL);

    attachedCount =
        WdfCollectionGetCount(
            NatsxTargetDevices);

    for (ULONG index = 0;
         index < attachedCount;
         ++index) {
        WDFDEVICE candidate =
            (WDFDEVICE)WdfCollectionGetItem(
                NatsxTargetDevices,
                index);

        if (NatsxGetDeviceContext(candidate)->UsbDevice !=
            WDF_NO_HANDLE) {
            readyCount++;
        }
    }

    WdfWaitLockRelease(
        NatsxTargetDevicesLock);

    *AttachedTargetCount =
        attachedCount;
    *ReadyUsbTargetCount =
        readyCount;
}

static NTSTATUS
NatsxReferenceSingleAttachedTarget(
    _Out_ WDFDEVICE* Device
    )
{
    *Device =
        WDF_NO_HANDLE;

    WdfWaitLockAcquire(
        NatsxTargetDevicesLock,
        NULL);

    ULONG count =
        WdfCollectionGetCount(
            NatsxTargetDevices);

    if (count == 1) {
        WDFDEVICE target =
            (WDFDEVICE)WdfCollectionGetItem(
                NatsxTargetDevices,
                0);

        WdfObjectReference(
            target);
        *Device =
            target;
    }

    WdfWaitLockRelease(
        NatsxTargetDevicesLock);

    if (count == 0) {
        return STATUS_DEVICE_NOT_READY;
    }

    if (count > 1) {
        return STATUS_DEVICE_BUSY;
    }

    return STATUS_SUCCESS;
}

static NTSTATUS
NatsxProbeAoaProtocolRawUrb(
    _In_ WDFDEVICE Device,
    _Out_ PUSHORT AoaProtocolVersion,
    _Out_ PULONG UsbStatus,
    _Out_ PULONG BytesTransferred
    )
{
    *AoaProtocolVersion =
        0;
    *UsbStatus =
        0;
    *BytesTransferred =
        0;

    USHORT protocolVersion =
        0;

    URB urb;
    RtlZeroMemory(
        &urb,
        sizeof(urb));

    UsbBuildVendorRequest(
        &urb,
        URB_FUNCTION_VENDOR_DEVICE,
        sizeof(struct _URB_CONTROL_VENDOR_OR_CLASS_REQUEST),
        USBD_TRANSFER_DIRECTION_IN | USBD_SHORT_TRANSFER_OK,
        0,
        AOA_GET_PROTOCOL,
        0,
        0,
        &protocolVersion,
        NULL,
        sizeof(protocolVersion),
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

    *UsbStatus =
        (ULONG)urb.UrbHeader.Status;

    if (NT_SUCCESS(status)) {
        *BytesTransferred =
            urb.UrbControlVendorClassRequest.TransferBufferLength;
        *AoaProtocolVersion =
            protocolVersion;
    }

    WdfObjectDelete(
        physicalTarget);

    return status;
}

static NTSTATUS
NatsxReferenceReadyTarget(
    _Out_ WDFDEVICE* Device
    )
{
    *Device =
        WDF_NO_HANDLE;

    WDFDEVICE readyDevice =
        WDF_NO_HANDLE;
    ULONG readyCount = 0;

    WdfWaitLockAcquire(
        NatsxTargetDevicesLock,
        NULL);

    ULONG count =
        WdfCollectionGetCount(
            NatsxTargetDevices);

    for (ULONG index = 0;
         index < count;
         ++index) {
        WDFDEVICE candidate =
            (WDFDEVICE)WdfCollectionGetItem(
                NatsxTargetDevices,
                index);

        if (NatsxGetDeviceContext(candidate)->UsbDevice ==
            WDF_NO_HANDLE) {
            continue;
        }

        readyDevice =
            candidate;
        readyCount++;

        if (readyCount > 1) {
            break;
        }
    }

    if (readyCount == 1) {
        WdfObjectReference(
            readyDevice);
        *Device =
            readyDevice;
    }

    WdfWaitLockRelease(
        NatsxTargetDevicesLock);

    if (readyCount == 0) {
        return STATUS_DEVICE_NOT_READY;
    }

    if (readyCount > 1) {
        return STATUS_DEVICE_BUSY;
    }

    return STATUS_SUCCESS;
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
