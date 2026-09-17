# OptiC's Warehouse Loft

**Created by OptiC**

A supplied Unity scene package containing the Loft scene, materials, textures, baked lighting, reflection data, music and Unity Readme scripts. Despite the archive name, inventory contains a .unity scene and no separate .prefab files.

## Required: ProBuilder

**This version requires ProBuilder. Install it BEFORE importing the package or opening the Loft scene. Tested version: 6.1.2.**

Without ProBuilder, the supplied scene reports 814 missing script components. Installing ProBuilder 6.1.2 resolved all of them in the maintainer test. ProBuilder is not bundled in the download.

## Setup
1. Create a fresh Unity **6000.3.21f1 Universal 3D (URP)** project.
2. Open Unity's **Package Manager**, select **Unity Registry**, search for **ProBuilder**, and install it. To select the tested version explicitly, use **Install package by name**, enter `com.unity.probuilder`, and set version `6.1.2`.
3. Wait for ProBuilder installation and script compilation to finish.
4. In Creator Hub **0.1.7+**, select your project and use the listing's import action. Setup **0.3.1+** and MCP companion **2.7.2+** also support this package. Review and confirm Unity's import dialog. Older apps can download and extract the ZIP below, then import its `.unitypackage` manually.
5. Open **Assets/Scenes/Loft.unity**.
6. Save your own working copy after checking the scene. Customise it under the supplied terms.

Import into a fresh project first: the package uses generic Assets/Materials and Assets/Scenes paths. UIElements is required by the included Unity Readme scripts; audio is required for the included music. These modules must be available in the target project.

The contributor reports Quest 2, Quest 3, PC VR and PC testing, with no known issues. Later Unity versions and device behaviour have not been independently verified here.

## Publication status
Updated apps offer project import of the verified Unity package. The ZIP link below remains available for manual import and older apps.

## Manual download

[Download the original ZIP](https://cdn.sidequestvr.com/file/4601609/optics-warehouse-loft-unity-urp-ver-6000321f1-prefab.zip)

SHA-256: cb7d9d949c15169069a235518c5bfdb8dbed29e5c3bceea4835392c8e2ad6330
