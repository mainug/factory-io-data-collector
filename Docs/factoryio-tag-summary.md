# Factory I/O tag export

- Source scene: `ProductionLine.factoryio`
- Scene saved: 2026-7-21
- CurrentDriver value: `64`
- Total scene signals: 223

## Scene signal counts

| Type | Count |
|---|---:|
| AnalogueInput | 8 |
| AnalogueOutput | 8 |
| BinaryInput | 85 |
| BinaryOutput | 117 |
| IntInput | 1 |
| IntOutput | 4 |

## Drivers with saved channel mappings

| Driver | Mapped signals |
|---|---:|
| SiemensS7PLCSIM | 171 |

The scene contains channel mappings for `SiemensS7PLCSIM`. The `ModbusTCPServer` configuration has no channel mappings yet. To control the scene directly from WinForms over Modbus TCP, switch the Factory I/O driver and configure its I/O point counts and tag mappings first.

See [factoryio-tag-map.csv](factoryio-tag-map.csv) for all mapped tags. `SceneAddress` is the internal scene address; `Channel` is the actual channel number used by the configured driver.
