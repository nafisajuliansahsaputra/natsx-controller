#pragma once

#include <ntddk.h>

#define NATSX_AOA_BOOTSTRAP_PROTOCOL_VERSION 1u
#define NATSX_AOA_BOOTSTRAP_DRIVER_BUILD 5u

// Retained as a stable protocol identifier for diagnostics/backward
// compatibility. Build 3 moved user-mode IOCTL access to a sideband control
// device rather than exposing the filter's PnP stack directly. Build 4 keeps
// that control plane alive independently of WDFUSBDEVICE readiness and adds a
// read-only target-readiness diagnostic. Build 5 exposes the exact NTSTATUS
// and attempt count from WdfUsbTargetDeviceCreateWithParameters so physical
// failures can be diagnosed without sending START_AOA.
// {54E7A3A1-01F0-41B8-B397-75E2A6D42C11}
DEFINE_GUID(
    GUID_DEVINTERFACE_NATSX_AOA_BOOTSTRAP,
    0x54e7a3a1,
    0x01f0,
    0x41b8,
    0xb3, 0x97, 0x75, 0xe2, 0xa6, 0xd4, 0x2c, 0x11);

#define FILE_DEVICE_NATSX_AOA_BOOTSTRAP 0xA361

#define IOCTL_NATSX_AOA_GET_VERSION \
    CTL_CODE( \
        FILE_DEVICE_NATSX_AOA_BOOTSTRAP, \
        0x800, \
        METHOD_BUFFERED, \
        FILE_READ_DATA)

#define IOCTL_NATSX_AOA_START \
    CTL_CODE( \
        FILE_DEVICE_NATSX_AOA_BOOTSTRAP, \
        0x801, \
        METHOD_BUFFERED, \
        FILE_READ_DATA | FILE_WRITE_DATA)

#define IOCTL_NATSX_AOA_GET_STATUS \
    CTL_CODE( \
        FILE_DEVICE_NATSX_AOA_BOOTSTRAP, \
        0x802, \
        METHOD_BUFFERED, \
        FILE_READ_DATA)

typedef struct _NATSX_AOA_VERSION_RESPONSE {
    ULONG ProtocolVersion;
    ULONG DriverBuild;
} NATSX_AOA_VERSION_RESPONSE, *PNATSX_AOA_VERSION_RESPONSE;

typedef struct _NATSX_AOA_START_RESPONSE {
    USHORT AoaProtocolVersion;
    USHORT Reserved;
} NATSX_AOA_START_RESPONSE, *PNATSX_AOA_START_RESPONSE;

typedef struct _NATSX_AOA_STATUS_RESPONSE {
    ULONG ProtocolVersion;
    ULONG DriverBuild;
    ULONG AttachedTargetCount;
    ULONG ReadyUsbTargetCount;
    NTSTATUS LastUsbTargetCreateStatus;
    ULONG UsbTargetCreateAttemptCount;
} NATSX_AOA_STATUS_RESPONSE, *PNATSX_AOA_STATUS_RESPONSE;
