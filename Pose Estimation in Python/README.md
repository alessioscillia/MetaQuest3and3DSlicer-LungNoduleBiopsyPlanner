# Pose Estimation in Python

This folder contains the Python pose estimation server used for needle tracking using cylindrical-marker.

The server receives camera frames from the Meta Quest 3 Unity application, detects the cylindrical marker attached to the instrument, estimates its 6-DOF pose using OpenCV, and returns the pose to Unity as JSON.

## Role in the Full System

This component is responsible for the needle tracking pipeline:

1. Receive JPEG frames from Unity.
2. Segment the cylindrical marker.
3. Detect and identify marker keypoints.
4. Estimate marker pose with `solvePnPRansac`.
5. Return marker translation, rotation, reprojection error, and inlier count.
6. Support camera calibration and debug frame export.

Unity uses this pose to visualize the tracked instrument and compute deviation from the planned biopsy trajectory.

## Folder Contents

- `pose_server.py`  
  Flask server used at runtime by the Unity application.

- `hsv_inspector.py`  
  Utility script for inspecting HSV values and tuning marker segmentation thresholds.

- `requirements.txt`  
  Python dependencies for the pose estimation environment.

- `cylmarker/`  
  Cylindrical marker detection and pose estimation package adapted from the original Cylmarker implementation.

- `data/`  
  Runtime configuration and marker definition files.

## Data Files
The server expects a data directory containing:

- config.yaml
Marker segmentation and detection parameters.

- camera_calibration.yaml
Camera intrinsic matrix and distortion coefficients, received by the Meta Quest. 

- pattern.yaml
Cylindrical marker binary pattern definition.

- marker.yaml
3D marker geometry and feature positions.

- marker.svg
Printable cylindrical marker.