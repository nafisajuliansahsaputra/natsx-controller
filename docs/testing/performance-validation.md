# Performance and soak validation

This document separates deterministic repository performance guards from physical validation that must be completed on the target Windows PC and Android phone before a production tag.

## Automated hot-path allocation guard

`HotPathAllocationTests.AcceptedRealtimeState_CoreHotPathDoesNotAllocatePerFrame` exercises 100,000 accepted controller states after warmup and uses `GC.GetAllocatedBytesForCurrentThread`.

The budget is intentionally tight enough to catch a future per-frame managed-object allocation in the core accepted-state path while allowing small one-time runtime noise.

The transport/network codecs perform cryptography and OS I/O outside this micro-guard. Their end-to-end cost is covered by the physical CPU/soak procedure below.

## Windows Receiver CPU and memory capture

Start the installed Receiver, establish the normal trusted controller session, and play/use the controls continuously enough to exercise realtime input, health tracking, rumble, and Smart Auto.

From the repository root, run:

```powershell
.\windows\eng\profile-receiver.ps1 -DurationMinutes 30
```

For the long soak:

```powershell
.\windows\eng\profile-receiver.ps1 -DurationMinutes 120 -OutputPath artifacts/performance/windows-receiver-profile-2h.csv
```

The script writes raw CSV samples and a JSON summary containing normalized CPU, working set, private memory, thread count, and handle count.

Review for:

- sustained CPU growth rather than short handover/reconnect spikes;
- working/private memory that trends upward continuously;
- thread/handle counts that grow after repeated reconnects;
- Receiver exit or virtual-controller loss.

Do not set a universal CPU percentage threshold in CI: hardware, power plan, Windows security features, and active transport materially change the result. Record the validation machine and compare the 30-minute and 2-hour runs on the same machine.

## Required physical 30-minute soak

Run at least 30 minutes with:

- active gameplay input;
- Wi-Fi as active authority for part of the run;
- USB plug/takeover/unplug/fallback cycles;
- Bluetooth ready/recovery when available;
- rumble/output events;
- no manual reconnect after a transport loss.

Pass criteria:

- no stuck input;
- no Receiver/Android crash;
- no virtual Xbox controller recreation/loss;
- authority returns to the best healthy transport;
- no continuously growing resource trend;
- pairing/trust remains intact.

## Required physical 2-hour soak

Run the same workload for two hours, including repeated idle-to-active transitions.

Pass criteria are the same as the 30-minute soak, with additional attention to memory/thread/handle stability and Android thermal/battery behavior.

## Windows sleep/resume

With a trusted session active:

1. put Windows to sleep;
2. leave the Android app running;
3. resume Windows;
4. verify stale input is neutral;
5. verify the Receiver recovers without recreating pairing;
6. verify Wi-Fi reconnect and USB/Bluetooth candidates can rejoin;
7. verify the same virtual Xbox device becomes usable again.

Repeat once with USB attached before sleep and once without USB.

## Android battery review

Use Android's normal system battery page for a user-facing check and, during engineering validation, optionally use Android platform battery diagnostics.

Compare a representative 30-minute controller session against an equivalent 30-minute screen-on idle period on the same phone and similar brightness/network conditions.

Record:

- battery percentage delta;
- device temperature/thermal warning behavior;
- whether the app is listed as unusually high background consumption;
- whether the foreground service remains stable;
- whether screen-awake behavior exists only while gameplay UI requires it.

The product does not require ADB at runtime. Any ADB-based engineering measurement is validation tooling only.

## Recording evidence

Store validation notes under `docs/testing/results/` with:

- date;
- app commit;
- Windows version/build;
- PC model;
- Android device/model and OS version;
- active transport sequence;
- profile summary paths;
- observed failures/recoveries;
- pass/fail for each M14 gate.

Do not mark the 30-minute, 2-hour, sleep/resume, CPU, or battery TODO items complete without physical evidence.
