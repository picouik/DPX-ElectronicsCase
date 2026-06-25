# DPX Electronics Case

Server-side SPT 4.0.13 mod that adds the DPX Electronics Case, a configurable storage case for electronics and advanced technology parts.

## Features

- Default trader: Skier LL1
- Default price: 250000 roubles
- Default external size: 3x3
- Default internal grid: 10x10
- Default accepted category: Electronics
- Uses a custom graphite/PCB Items Case bundle
- No BepInEx plugin required
- Does not modify SPT_Data or vanilla files

## Configuration

The mod creates `config/config.json` automatically if it does not exist.

```json
{
  "caseSize": {
    "width": 3,
    "height": 3
  },
  "internalGrid": {
    "width": 10,
    "height": 10
  },
  "price": 250000,
  "trader": "Skier",
  "loyaltyLevel": 1,
  "acceptedCategories": [
    "57864a66245977548f04a81f"
  ],
  "acceptedItems": []
}
```

### Limits

- `caseSize.width`: 1 to 10
- `caseSize.height`: 1 to 10
- `internalGrid.width`: 1 to 20
- `internalGrid.height`: 1 to 20
- `price`: minimum 1
- `loyaltyLevel`: 1 to 4
- `acceptedCategories`: valid 24-character Mongo IDs only
- `acceptedItems`: valid 24-character Mongo IDs only

Supported trader names:

- Prapor
- Therapist
- Fence
- Skier
- Peacekeeper
- Mechanic
- Ragman
- Jaeger

Invalid or missing values fall back to safe defaults. The filter will never be left empty; if all configured IDs are invalid, the Electronics category is restored automatically.

Avoid reducing the internal grid size after storing items inside the case.

The icon, model, prefab, and bundle are not configurable.

## Installation

Extract the release archive into your SPT root folder. The mod should end up in:

```text
SPT/user/mods/DPX-ElectronicsCase/
```

Start the SPT server and check the configured trader.

## Compatibility

Built for SPT 4.0.13 build 40087.
