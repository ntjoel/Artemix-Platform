# Virtual Conveyor I/O Trigger - 2026-04-27

## Scope

This note documents the first machine-side runtime used when the PCIE-1756 I/O board is wired on the real machine but the PCIE-1884 encoder board is not used.

The runtime uses:

- real digital input from the product photocell
- real digital outputs for camera / lighting commands
- a virtual conveyor speed entered manually in `m/min`
- existing machine intervention points in millimeters

This keeps the physical machine mapping separated from recipe vision parameters.

## Field Wiring Summary

### Input ADAM-3951-BE on CON1

Use the first input bank:

```text
24V sensor supply -> photocell brown
0V sensor supply  -> photocell blue / grey
photocell output  -> IDI0 terminal 1
0V supply         -> ECOM0 terminal 17
```

Software mapping:

```text
IN_PRODUCT_PHOTOCELL -> PCIE-1756-BE DI00
```

Expected result:

- photocell occupied: ADAM input LED ON
- HMI / I/O diagnostics: `DI00` active
- standard runtime: product zero is created

### Output ADAM-3951-BE on CON2

For PCIE-1756 isolated outputs use the sink-output wiring already validated on bench:

```text
+24V supply -> PCOM0 terminal 17
0V supply   -> IGND terminal 21
```

For the current two-camera machine flow:

```text
IDO0 terminal 1 -> OUT_SPARE_DO00 / leave free unless explicitly used for diagnostics
IDO1 terminal 2 -> OUT_CAMERA_SIDE_TRIGGER / side camera trigger command
IDO2 terminal 3 -> OUT_CAMERA_TOP_TRIGGER / top camera trigger command
```

Software mapping:

```text
OUT_SPARE_DO00          -> PCIE-1756-BE DO00
OUT_CAMERA_SIDE_TRIGGER -> PCIE-1756-BE DO01
OUT_CAMERA_TOP_TRIGGER  -> PCIE-1756-BE DO02
```

Important: PCIE-1756 outputs are sink type. They do not source `+24V`. If a camera or illuminator trigger input requires PNP/source `+24V`, use an interface relay/opto-adapter or a proper NPN-to-PNP converter.

## Runtime Logic

When virtual conveyor mode is enabled:

1. The photocell rising edge creates a tracked product at the current virtual encoder count.
2. The virtual conveyor advances the main encoder count using the configured speed.
3. Existing intervention points are evaluated exactly as if the count came from PCIE-1884.
4. When `CAMERA_TRIGGER_TOP`, `CAMERA_TRIGGER_SIDE` or other configured points are reached, the mapped outputs are pulsed.

Formula:

```text
speed_mm_s = speed_m_min * 1000 / 60
delta_counts = speed_mm_s * elapsed_s * counts_per_mm
counts_per_mm = PulsesPerRevolution / MillimetersPerRevolution
```

## HMI Configuration

In the I/O page:

1. Keep mode on `HW` so digital inputs and outputs use the real PCIE-1756 board.
2. Open `Encoder Setup`.
3. Enable `Virtual conveyor in HW`.
4. Enter the conveyor speed in `m/min`, or use pieces/min with product pitch.
5. Keep `Conveyor running` enabled.
6. Configure the physical photocell position and intervention points in millimeters.
7. Save the machine configuration.

The feature is stored in `machine_runtime_config.xml` under:

```xml
<VirtualConveyorEnabled>true</VirtualConveyorEnabled>
<VirtualConveyorSpeedMetersPerMinute>18</VirtualConveyorSpeedMetersPerMinute>
<VirtualConveyorUsePiecesPerMinute>false</VirtualConveyorUsePiecesPerMinute>
<VirtualConveyorPiecesPerMinute>120</VirtualConveyorPiecesPerMinute>
<VirtualConveyorProductPitchMm>150</VirtualConveyorProductPitchMm>
```

## Machine vs Recipe Boundary

Per the current project baseline:

- physical I/O mapping remains machine configuration
- physical distances and intervention defaults remain machine configuration
- camera exposure/inspection parameters remain recipe configuration
- camera trigger delays remain present in the recipe for compatibility, but on I/O-driven machines they are not written to camera hardware at startup
- future recipe-specific offsets can be layered as trims, but must not duplicate board/channel mapping inside the recipe

## First Test Procedure

1. Verify `DI00` changes when the photocell is occupied.
2. Force `DO01` and `DO02` manually and verify the side/top camera trigger chains.
3. Enable virtual conveyor mode in `HW`.
4. Set a slow speed such as `3.00 m/min`.
5. Set `CAMERA_TRIGGER_TOP` and `CAMERA_TRIGGER_SIDE` offsets close to zero for first validation.
6. Pass a product or hand through the photocell.
7. Confirm in the event log:
   - product detected
   - intervention point reached
   - `DO01` and `DO02` pulse at the configured points
8. Increase offsets and speed only after the sequence is stable.
