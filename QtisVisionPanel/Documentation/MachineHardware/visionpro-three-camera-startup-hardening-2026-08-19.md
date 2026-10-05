# VisionPro Three-Camera Startup Hardening

## Scope

This note documents the startup correction introduced for VPP recipes with
three or more camera jobs, including the verified scenario:

- recipe: `Dual_Mutishot`;
- jobs: `Top`, `Left`, `Rear`;
- third camera: Blackfly S `BFS-PGE-50S5` through a VisionPro GigE FIFO.

The change affects runtime startup and diagnostics only. It does not change
recipe XML, `Config.xml`, machine I/O, trigger timing, MultiShot behavior or the
database.

## Previous symptom

At the first application startup only `Top` and `Left` could be visible. The
`Rear` panel appeared after loading another recipe and then reloading the
three-camera recipe. Continuous-run recovery could fail because the Rear job
had not entered running state.

Two independent startup weaknesses produced this behavior:

1. `CameraContainerViewModel` removed a valid VPP job from the UI when its
   `AcqFifo.FrameGrabber` was temporarily unavailable during the first refresh.
2. `CognexJobManager.RunContinuous` ignored every exception raised while
   starting an individual job and did not verify that all jobs were running.

The Blackfly model is not rejected by the HMI. A third-party GigE camera can
need more enumeration time than the other cameras; VisionPro must still expose
a valid acquisition FIFO for that job.

## Runtime behavior from 3.0.9.4

- every supported semantic job in the active VPP remains visible;
- a temporarily unavailable grabber is reported as
  `CAMERA_VIEW_HARDWARE_PENDING`, but its panel is not removed;
- each VisionPro job is started and verified independently;
- an acquisition FIFO can settle for a bounded interval before each attempt;
- a job receives at most three start attempts;
- a start attempt is successful only when `RunningSyncBoolean` becomes true;
- if one job still fails, every job is stopped to prevent partial inspection;
- the machine runtime remains stopped and records the exact failing job,
  acquisition state, FIFO type, grabber name and serial;
- automatic recovery includes the latest per-job failure detail.

When the first VPP load contains an invalid GigE job, startup also performs a
bounded camera-discovery recovery:

1. count the GigE acquisition jobs required by the VPP;
2. poll the Cognex FrameGrabber inventory for at most 20 seconds;
3. require the expected camera count for three consecutive samples;
4. wait a short additional interval for FIFO-to-grabber binding;
5. if a job is still invalid, shut down the manager and clear all static
   job/ToolBlock references;
6. wait for GigE resource release, then reload the same VPP;
7. repeat the full reload at most three times;
8. continue only with the normal all-jobs verified start sequence.

This reproduces automatically the successful recipe round trip previously
performed by the operator. Inventory readiness and job readiness are kept
separate: `available=3` does not prove that the deserialized Rear FIFO already
owns its specific FrameGrabber. All waits and reloads are bounded, and the path
is skipped entirely when every FIFO is valid at the first load.

The operation timeout is 20 seconds so a multi-camera VPP has enough time for
bounded retries without allowing an unlimited Cognex call to block startup.

## Expected log sequence

For a successful three-camera startup:

```text
Job Mapping created with 3 entries
VISIONPRO_GIGE_DISCOVERY_WAIT|expected=3|invalid=Rear (...)
VISIONPRO_GIGE_INVENTORY|available=2|expected=3|...
VISIONPRO_GIGE_INVENTORY|available=3|expected=3|...
VISIONPRO_GIGE_DISCOVERY_READY|available=3|expected=3|...
VISIONPRO_GIGE_BINDING_PENDING_AFTER_DISCOVERY|invalid=Rear (...)
VISIONPRO_VPP_RELOAD_AFTER_GIGE_DISCOVERY|attempt=1/3|...
VISIONPRO_GIGE_MANAGER_RELEASED|attempt=1|settleMs=750
VISIONPRO_VPP_RELOAD_RESULT|status=ready|attempt=1|jobs=3
VISIONPRO_JOB_START_ATTEMPT|jobIndex=0|job=Top|...
VISIONPRO_JOB_RUNNING|jobIndex=0|job=Top|...
VISIONPRO_JOB_START_ATTEMPT|jobIndex=1|job=Left|...
VISIONPRO_JOB_RUNNING|jobIndex=1|job=Left|...
VISIONPRO_JOB_START_ATTEMPT|jobIndex=2|job=Rear|...
VISIONPRO_JOB_RUNNING|jobIndex=2|job=Rear|...
VISIONPRO_CONTINUOUS_START_COMPLETE|jobs=3
```

`CAMERA_VIEW_HARDWARE_PENDING` is acceptable only as a transient startup
warning. If the Rear camera does not become ready, look for:

```text
VISIONPRO_JOB_START_RETRY|jobIndex=2|job=Rear|...
VISIONPRO_CONTINUOUS_START_INCOMPLETE|...
```

The diagnostic payload identifies whether the job has an invalid acquisition
state, a missing FIFO, a pending grabber or a specific VisionPro exception.

Use the discovery logs to classify a startup failure:

| Evidence | Meaning | Commissioning action |
|---|---|---|
| `available=2`, `expected=3`, then discovery timeout | Blackfly is not enumerated by Cognex | Check power, link, NIC subnet, unique IP, driver and exclusive ownership |
| `available=3`, then one or more reload `status=invalid` | Camera exists but Rear FIFO is not yet bound | Let the bounded sequence finish; do not press Start during bootstrap |
| reload `status=load-failed` | Il VPP non e' stato deserializzato nel tentativo corrente | Leggere `error`; il sistema riprova fino al limite senza usare un manager parziale |
| `VISIONPRO_VPP_RELOAD_EXHAUSTED` | Camera inventory is complete but Rear never binds | Check `RearCameraSerial`, VPP acquisition device, video format, IP/NIC and exclusive ownership |
| reload `status=ready` but continuous start fails | FIFO binds but the Rear job cannot enter RunContinuous | Inspect the per-job VisionPro exception and job RunMode |

## Cause Found in the Field After 3.0.9.4 - Fixed in 3.0.9.9

The `available=3`/reload path above never ran at all in one field startup: the
log jumped straight from `Job Rear has invalid AcqFifoState: Invalid` (the
per-job message logged inside `Initialize()` at VPP load) to
`VISIONPRO_JOB_START_RETRY|job=Rear|...RunContinuous` failures, with none of
`VISIONPRO_GIGE_DISCOVERY_WAIT`, `VISIONPRO_GIGE_INVENTORY` or any
`VISIONPRO_VPP_RELOAD_*` entry in between.

**Cause:** `InitializeCognexManagerWithGigERecoveryAsync` called
`GetInvalidGigEAcquisitionJobs()` immediately after
`CreateAndInitializeCognexManager`, with no settle delay. That method (and
`GetGigEAcquisitionJobCount()`) both filter on `IsGigEAcquisitionJob`, which
classifies a job as GigE only once its `AcqFifo` has resolved to a concrete
`CogAcqFifoGigE` instance; immediately after VPP deserialization `job.AcqFifo`
can still be `null` for the Rear job, so `IsGigEAcquisitionJob` returned false
and the job was silently excluded from the invalid-job list - not because it
was ready, but because it wasn't recognized as GigE yet. With
`invalidJobs.Count == 0`, the method returned immediately, skipping the entire
bounded discovery/reload safety net added in 3.0.9.4. By the time
`TryStartJobContinuous` ran for Rear (after several more startup steps), its
FIFO had since resolved to `CogAcqFifoGigE`, but `AcqFifoState` was still
`Invalid` - exactly the case the 3.0.9.4 recovery exists to handle, just
reached too late and with no reload attempted.

Fix: give the FIFO the same settle window (`GigEStartupBindingSettleMs`,
already used later in the same method) before the very first classification,
so a not-yet-typed Rear FIFO isn't misread as "not a GigE job" and skipped.
No change to `IsGigEAcquisitionJob`, `IsAcquisitionReady`, the reload loop, or
any other camera's startup path.

New expected evidence: if this classification race is the cause, the log
should now show `VISIONPRO_GIGE_DISCOVERY_WAIT` (or
`VISIONPRO_GIGE_BINDING_READY`) immediately after the per-job
`AcqFifoState: Invalid` line, instead of jumping straight to
`VISIONPRO_JOB_START_RETRY`.

## Machine validation

1. Power the three cameras and wait for their network links.
2. Start the HMI directly with `Dual_Mutishot` as the last recipe.
3. Confirm that `Top`, `Left` and `Rear` panels are present without a recipe
   round trip.
4. If recovery is needed, confirm inventory `available=3|expected=3`, manager
   release and final reload result `status=ready`.
5. Confirm one `VISIONPRO_JOB_RUNNING` entry for each job.
6. Confirm `VISIONPRO_CONTINUOUS_START_COMPLETE|jobs=3`.
7. Pass at least ten products and verify that all three roles produce results.
8. Stop and start the machine from the HMI and repeat the result check.
9. Restart the application three times to cover GigE enumeration timing.

If the Rear job still fails, verify in commissioning:

- the QuickBuild job name is exactly `Rear` or maps explicitly to role `Rear`;
- `RearCameraSerial` contains the Blackfly serial used by the VPP;
- VisionPro QuickBuild can open and run all three jobs after a cold PC start;
- the Blackfly has a unique IP address and is reachable on the acquisition NIC;
- the camera is not already owned by another application;
- the VPP Rear acquisition FIFO is saved against the intended camera and video
  format.

## Rollback

Rollback requires restoring version `3.0.9.3`. No configuration or database
migration is required in either direction.
