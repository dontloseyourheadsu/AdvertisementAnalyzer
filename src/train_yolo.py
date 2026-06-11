import sys
import os
import argparse
from ultralytics import YOLO

def train(epochs, api_key):
    print(f"Starting training for {epochs} epochs...")
    
    # We check if dataset.yaml exists
    yaml_path = "dataset/data.yaml"
    if not os.path.exists(yaml_path):
        print(f"Warning: dataset config '{yaml_path}' not found. Generating default mock config...")
        # Create a mock data.yaml for compilation/testing
        os.makedirs("dataset", exist_ok=True)
        with open(yaml_path, "w") as f:
            f.write(
                "train: ../dataset/images\n"
                "val: ../dataset/images\n"
                "nc: 1\n"
                "names: ['billboard']\n"
            )
            
    print(f"Training on dataset: {yaml_path}")
    model = YOLO("yolov8n.pt")
    
    # Check if images directory exists or create dummy directory structure
    os.makedirs("dataset/images", exist_ok=True)
    
    # Run training
    # In a real pipeline, we'd ensure images exist. We limit epochs to 1 if it's dummy data.
    model.train(data=yaml_path, epochs=epochs, imgsz=640)
    print("Training finished! Exporting model to ONNX...")
    path = model.export(format="onnx")
    print(f"Model exported successfully to: {path}")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="YOLOv8 Training Script")
    parser.add_argument("--epochs", type=int, default=20, help="Number of epochs")
    parser.add_argument("--api-key", type=str, default="", help="API Key for dataset download")
    args = parser.parse_args()
    train(args.epochs, args.api_key)
