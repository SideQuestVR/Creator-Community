# OptiC's Water

**Kijai and Cause — made for OptiC**

Simple URP water shader with animated normals, cubemap reflections, transparency and optional depth colouring/shoreline foam.

## Setup
1. Use a Unity 6 project with Universal Render Pipeline 17.x installed. Built-in and HDRP are not supported by this source.
2. Import the unitypackage using Unity's import review dialog.
3. Create a material and select **OptiC/Cause Water**, then assign it to your water mesh.
4. Supply your own normal map in Normal_A and a reflection cubemap, or configure scene reflection probes. Adjust tiling, pan speed, normal strength, smoothness and opacity.
5. For optional depth colouring and foam, enable the pipeline/camera depth texture and the material's UseDepth option, then adjust shallow/deep colours and foam distances.

Contains the supplied shader only, plus these instructions. No original ZIP, normal maps, cubemaps, materials or example world were supplied. The preview is the user-supplied example screenshot; its scene and textures are not included. Catalogue version 0.1.0 is a maintainer packaging version, not an author release number.

OptiC's original post reports Unity 6 URP, Quest 2, Quest 3 and PC use. This is contributor-reported compatibility, not independent headset testing.
