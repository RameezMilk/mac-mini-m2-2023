# Mac mini (2023, M2) — open 3D model

A real-scale 3D model of the **2023 Apple Mac mini with the base M2 chip**, released under **CC BY 4.0**. You get a standalone `.glb` and an editable Unity prefab with the procedural builder script that generates it.

**[⬇ Download mac-mini-m2-2023.glb (256 KB)](https://github.com/RameezMilk/mac-mini-m2-2023/releases/download/v1.0/mac-mini-m2-2023.glb)** · [Release notes](https://github.com/RameezMilk/mac-mini-m2-2023/releases/tag/v1.0)

| | |
|---|---|
| File | `mac-mini-m2-2023.glb`, self-contained, no textures; passes the Khronos glTF Validator with 0 errors and 0 warnings |
| Geometry | 5,305 triangles · 14 meshes · 8 PBR materials |
| Scale | metres, real size: 0.197 × 0.0358 × 0.197 m (Apple spec: 197 × 197 × 35.8 mm) |
| Orientation | Y‑up, front faces +Z, pivot at bottom-centre on the floor plane |
| Extensions | none required (core glTF 2.0, metallic-roughness) |

## Files

```
mac-mini-m2-2023.glb      the model
index.html                minimal viewer (rotate and zoom)
unity/MacMini_M2_2023/    Unity prefab, meshes, materials, builder script
LICENSE                   CC BY 4.0
```

## View it

Open the `.glb` in any glTF viewer, or serve this folder and open `index.html` (a `.glb` can't be loaded from `file://`):

```sh
python3 -m http.server 8000   # then open http://localhost:8000/
```

To embed it on a page with [`<model-viewer>`](https://modelviewer.dev):

```html
<script type="module" src="https://cdn.jsdelivr.net/npm/@google/model-viewer@4.3.1/dist/model-viewer.min.js"></script>
<model-viewer src="mac-mini-m2-2023.glb" alt="3D model of a 2023 Mac mini (M2)"
  camera-controls environment-image="neutral" shadow-intensity="0.9"></model-viewer>
```

### Parts

The root `MacMini_M2_2023` contains `Enclosure`, `Base`, `RearPanel` and `StatusLight`. Each rear port is its own child of `RearPanel`: `PowerButton`, `Port_Power`, `Port_Ethernet`, `Port_Thunderbolt_1`, `Port_Thunderbolt_2`, `Port_HDMI`, `Port_USB_A_1`, `Port_USB_A_2`, `Port_Headphone` and `RearVent`. That way you can highlight or hide parts individually.

## Edit or re-export in Unity

Tested with Unity 6000.5 (URP) and glTFast 6.20.

1. Add `com.unity.cloud.gltfast` to your project (Package Manager → Add package by name).
2. Copy `unity/MacMini_M2_2023` **and** `unity/MacMini_M2_2023.meta` into your project's `Assets/`. Keep the `.meta` files so the prefab references resolve.
3. Drop `MacMini_M2_2023.prefab` into a scene, or open `MacMini_M2_2023_Preview.unity`.
4. Change dimensions or port layout in `Editor/MacMiniBuilder.cs`. All sizes are at the top of the file. Then run:
   - **Tools → Mac mini M2 → Build** regenerates the meshes, materials and prefab.
   - **Tools → Mac mini M2 → Export GLB** writes `mac-mini-m2-2023.glb` to the project root (**Export GLB As…** to choose a path).

Headless rebuild and export:

```sh
Unity -batchmode -quit -projectPath <your-project> \
  -executeMethod MacMiniM2.MacMiniBuilder.BatchBuildAndExport \
  -glbOut /path/to/mac-mini-m2-2023.glb
```

The materials use URP Lit; in the Built-in pipeline the builder falls back to `Standard` on rebuild. glTFast mirrors X on export, because glTF is right-handed and Unity left-handed; front stays +Z.

## Accuracy

Modelled from Apple's published dimensions and reference photos:

- **Enclosure:** silver aluminium unibody with rounded corners and filleted edges.
- **Underside:** the inset black circular base.
- **Front:** the front-right status light.
- **Rear I/O:** the dark rear panel. Left to right, seen from behind: power button, AC inlet, Gigabit Ethernet, 2× Thunderbolt 4, HDMI, 2× USB‑A, and the 3.5 mm jack below the USB‑A pair. Below the ports is a slotted vent.
- **Port detail:** every port is recessed, with tongues, contacts and pins where the real connector has them.

Approximated:

- The corner radius (15.5 mm), fillets and base diameter were measured from photos.
- Port horizontal spacing was adapted from M2 Pro rear photos down to the base M2's two Thunderbolt ports.
- The vent is simplified, and port labels aren't modelled.
- No Apple logo, regulatory text or internals.

Corrections are welcome.

## License

© Rameez Malik, licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). You may use, adapt and redistribute this model, including commercially, with attribution.

Not affiliated with or endorsed by Apple Inc. Apple, Mac and Mac mini are trademarks of Apple Inc. The license covers this model only, not Apple's trademarks.
