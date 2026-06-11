import sys
import os
import shutil
import kagglehub

def download(slug, target_dir):
    print(f"Downloading Kaggle dataset '{slug}' using kagglehub...")
    
    path = kagglehub.dataset_download(slug)
    print(f"Downloaded to cache: {path}")
    
    # Clean and recreate target_dir
    if os.path.exists(target_dir):
        print(f"Cleaning existing directory {target_dir}...")
        # If it's a symlink or directory
        if os.path.islink(target_dir):
            os.unlink(target_dir)
        else:
            shutil.rmtree(target_dir)
            
    os.makedirs(target_dir, exist_ok=True)
    
    # Copy all files from path to target_dir
    print(f"Moving files to {target_dir}...")
    for item in os.listdir(path):
        s = os.path.join(path, item)
        d = os.path.join(target_dir, item)
        if os.path.isdir(s):
            shutil.copytree(s, d, dirs_exist_ok=True)
        else:
            shutil.copy2(s, d)
    print("Dataset ready!")

if __name__ == "__main__":
    slug = sys.argv[1] if len(sys.argv) > 1 else "dataclusterlabs/ad-board"
    target = sys.argv[2] if len(sys.argv) > 2 else "dataset"
    download(slug, target)
