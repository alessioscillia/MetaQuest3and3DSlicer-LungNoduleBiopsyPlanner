# LungNoduleBiopsyPlanner - 3D Slicer Module

This folder contains the 3D Slicer module used to prepare the anatomical scene for the Meta Quest 3 application.

The module handles CT loading, segmentation, trajectory planning support, model export via HTTP server, and OpenIGTLink communication with Unity.

![3D Slicer Interface](docs/assets/img1.png)

## Role in the Full System

This module is responsible for the Slicer-side workflow:

1. Load a thoracic CT volume.
2. Segment relevant anatomy using TotalSegmentator.
3. Prepare planning structures for biopsy trajectory selection.
4. Use Percutaneous Approach Analysis and PortPlacement for trajectory planning.
5. Export the selected anatomical models and the needle that follows the planned trajectory as `model.glb`.
6. Start a local HTTP server on port `8080` so Unity can import the GLB model.
7. Start an OpenIGTLink server on port `18944` to exchange transforms and slice images with Unity.


## Folder Contents

- `LungNoduleBiopsyPlanner.py`  
  Main scripted Slicer module. Contains the widget logic, segmentation workflow, GLB export, web server launch and slice streaming logic.

- `Resources/UI/LungNoduleBiopsyPlanner.ui`  
  Qt Designer UI file loaded by the module.

- `Resources/Icons/LungNoduleBiopsyPlanner.png`  
  Module icon.

- `requirements.txt`  
  Python dependencies used by the Slicer-side code.

## Requirements

Tested with:

- 3D Slicer 5.10.0

Required Slicer extensions/modules:

- SlicerIGT
- SlicerOpenIGTLink
- TotalSegmentator
- Percutaneous Approach Analysis
- PortPlacement

Required Python packages:

- `numpy`
- `trimesh`
- `TotalSegmentator`
- `open3d`

`open3d` is required by the modified Percutaneous Approach Analysis workflow used in this project to speed up trajectory search.

## Installing the Module in Slicer

1. Open 3D Slicer.
2. Go to `Edit > Application Settings > Modules`.
3. Add this folder as an additional module path.
4. Restart Slicer.
5. Open the module from the `AR Surgical Procedures` category.

Make sure all required Slicer extensions are installed before loading the module.

## Main Features
 ### Segmentation workflow
 The module uses TotalSegmentator to generate anatomical structures required by the AR application, including:

- lungs
- nodules
- airways
- body/skin surface
- obstacle models for trajectory planning

### Trajectory Planning
Trajectory planning is supported through Slicer planning modules embedded into this module:

- Markups
- Percutaneous Approach Analysis
- PortPlacement

### OpenIGTLink Communication
The module starts an OpenIGTLink server on port 18944
This channel is usedo to:
- receive transform updated from Unity;
- stream slice images to Unity;
- keep the interactive slicing plane aligned between Unity and Slicer.

 Note that before activating OpenIGTLink, the module recenters the loaded CT volume to the origin. This keeps the Slicer coordinate space aligned with the Unity scene after GLB export.