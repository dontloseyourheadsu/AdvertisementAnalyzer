import os
import io
import json
from http.server import HTTPServer, BaseHTTPRequestHandler
from PIL import Image

# Huggingface packages
import torch
from transformers import AutoProcessor, AutoModelForCausalLM

print("Loading Florence-2 model...")
device = "cpu"
model_id = "microsoft/Florence-2-base"
model = AutoModelForCausalLM.from_pretrained(model_id, trust_remote_code=True).to(device)
processor = AutoProcessor.from_pretrained(model_id, trust_remote_code=True)
model.eval()
print("Florence-2 model loaded successfully!")

def run_florence(image, task_prompt, text_input=None):
    if text_input is None:
        prompt = task_prompt
    else:
        prompt = task_prompt + text_input
    inputs = processor(text=prompt, images=image, return_tensors="pt").to(device)
    with torch.no_grad():
        generated_ids = model.generate(
            input_ids=inputs["input_ids"],
            pixel_values=inputs["pixel_values"],
            max_new_tokens=1024,
            num_beams=3
        )
    generated_text = processor.batch_decode(generated_ids, skip_special_tokens=True)[0]
    parsed_answer = processor.post_process_generation(generated_text, task=task_prompt, image_size=(image.width, image.height))
    return parsed_answer

class VLMHandler(BaseHTTPRequestHandler):
    def do_POST(self):
        if self.path == '/refine':
            content_length = int(self.headers['Content-Length'])
            post_data = self.rfile.read(content_length)
            
            try:
                # Load image from bytes
                image = Image.open(io.BytesIO(post_data)).convert("RGB")
                
                # Run captioning
                caption_res = run_florence(image, "<MORE_DETAILED_CAPTION>")
                caption = caption_res.get("<MORE_DETAILED_CAPTION>", "An advertisement sign.")
                
                # Run OCR
                ocr_res = run_florence(image, "<OCR>")
                ocr_text = ocr_res.get("<OCR>", "")
                
                response = {
                    "visual_ocr": ocr_text,
                    "image_caption": caption
                }
                
                self.send_response(200)
                self.send_header('Content-Type', 'application/json')
                self.end_headers()
                self.wfile.write(json.dumps(response).encode('utf-8'))
                
            except Exception as e:
                self.send_response(500)
                self.end_headers()
                self.wfile.write(str(e).encode('utf-8'))
        else:
            self.send_response(404)
            self.end_headers()

def run(server_class=HTTPServer, handler_class=VLMHandler, port=5000):
    server_address = ('', port)
    httpd = server_class(server_address, handler_class)
    print(f"Starting VLM Service on port {port}...")
    httpd.serve_forever()

if __name__ == '__main__':
    run()
