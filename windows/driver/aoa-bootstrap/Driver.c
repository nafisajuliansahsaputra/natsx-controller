#include <ntddk.h>
#include <wdf.h>
#include <initguid.h>

#include "Public.h"

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD NatsxEvtDeviceAdd;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL NatsxEvtIoDeviceControl;

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
    WDF_IO_QUEUE_CONFIG queueConfig;
    NTSTATUS status;

    // Prototype is intentionally a pass-through filter. It must not replace
    // the OEM MTP/PTP function driver.
    WdfFdoInitSetFilter(DeviceInit);

    status = WdfDeviceCreate(
        &DeviceInit,
        WDF_NO_OBJECT_ATTRIBUTES,
        &device);

    if (!NT_SUCCESS(status)) {
        return status;
    }

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

    if (IoControlCode != IOCTL_NATSX_AOA_GET_VERSION) {
        NatsxForwardRequest(
            Queue,
            Request);
        return;
    }

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
    response->DriverBuild = 1u;

    WdfRequestCompleteWithInformation(
        Request,
        STATUS_SUCCESS,
        sizeof(*response));
}
