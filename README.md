# VR Plant Disease & Treatment System

A Unity-based VR application for plant disease diagnosis and treatment training.

## Project Structure

- `Assets/_Project/` — Unity scripts, shaders, prefabs, scenes, and data
- `Backend/` — FastAPI disease detection API with ML inference pipeline
- `Web/` — React frontend dashboard for CropScan results
- `Tools/` — Blender automation, leaf preparation, and eval scripts
- `Docs/` — Design documents, XR contract, and VR treatment concepts

## Features

- 9 plant species with disease visualization in VR (Meta Quest 2/3)
- Interactive farm and lab scenes with PlantVillage disease gallery
- Multi-stage disease recovery demo with weekly progression metrics
- XR session coordinator for headset pairing and scan dispatch
- FastAPI backend with EfficientNet-B0 disease classification model
- CropScan desktop launcher for real-time specimen scanning

## Setup

See `Docs/CropScan_XR_Implementation_Guide.md` for full setup instructions.

## Author

Bhargav M — bhargav.m23@iiits.in
