# GWM v1 — Unity Terrain export format

The Unity Editor command `Tools > Game Client > Export World for Go Server` writes one directory per exported Terrain:

```text
WorldExport/
  <scene-name>/
    <terrain-name>/
      manifest.json
      terrain.u16
```

The export is generated from the selected Terrain in the active scene, or from the first Terrain found in that scene if none is selected. It does not export terrain textures, lighting, vegetation, or separate scene colliders.

## manifest.json

A representative manifest looks like this (numbers depend on the selected terrain):

```json
{
  "format": "GWM",
  "formatVersion": 1,
  "sceneName": "SampleScene",
  "terrainName": "Terrain",
  "coordinateSystem": "Unity world coordinates; +Y is up; Terrain axes are unrotated (+X and +Z).",
  "origin": { "x": 0.0, "y": 0.0, "z": 0.0 },
  "size": { "x": 1000.0, "y": 200.0, "z": 1000.0 },
  "widthSamples": 513,
  "heightSamples": 513,
  "sampleSpacing": { "x": 1.953125, "z": 1.953125 },
  "encoding": "uint16",
  "byteOrder": "little-endian",
  "rowOrder": "z-ascending; x-ascending within each row",
  "firstSample": "(x=0, z=0)",
  "heightFormula": "worldY = origin.y + (sample / 65535.0) * size.y",
  "dataFile": "terrain.u16",
  "dataLengthBytes": 526338,
  "sha256": "<SHA-256 of terrain.u16>",
  "minimumSample": 0,
  "maximumSample": 65535,
  "minimumHeightWorld": 0.0,
  "maximumHeightWorld": 200.0
}
```

The example is illustrative, not a real export. The exporter writes the actual values for the selected Terrain.

## terrain.u16

- Raw binary file with no header; metadata is in `manifest.json`.
- Each height sample is an unsigned 16-bit integer (`uint16`) stored little-endian.
- A sample is quantized from Unity's normalized height value `h` in the range `[0, 1]` with `round(clamp(h, 0, 1) * 65535)`.
- Rows are ordered by increasing Terrain Z. Within every row, X increases. X changes fastest.
- The first two bytes describe the sample at local heightmap coordinate `(x=0, z=0)`.
- Expected byte length is `widthSamples * heightSamples * 2`.
- `sha256` in the manifest is calculated over the exact bytes of `terrain.u16`.

To recover an approximate world-space height:

```text
sample = uint16_little_endian(data[offset : offset + 2])
worldY = origin.y + (sample / 65535.0) * size.y
offset = (z * widthSamples + x) * 2
```

Sample grid coordinates are not the same as world coordinates. To map a world X/Z coordinate to the grid, subtract `origin`, then divide X by `sampleSpacing.x` and Z by `sampleSpacing.z`. The server's height-query implementation must define one consistent interpolation rule between samples and use it for all movement and collision checks.

## Supported transforms

GWM v1 supports an unrotated Terrain with a positive scale. The exporter includes the Terrain's world position and scaled world size in the manifest. It refuses rotated or negatively scaled Terrain rather than silently exporting incorrect coordinates.

## Validation on the Go server

When loading the files, the server should reject an unsupported `formatVersion`, verify the binary length, and compare the SHA-256 checksum before using height samples. The file format contains terrain heights only; object collision geometry (buildings, bridges, caves, and other non-heightfield shapes) requires a separate export format.
