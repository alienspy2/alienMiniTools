import torch
import argparse
from qwen_tts import Qwen3TTSModel

def check_model_device():
    ckpt = "Qwen/Qwen3-TTS-12Hz-1.7B-CustomVoice"
    device = "cuda:0"
    dtype = torch.bfloat16
    
    print(f"Loading model on {device} with {dtype}...")
    try:
        if torch.cuda.is_available():
            torch.cuda.reset_peak_memory_stats()
            mem_before = torch.cuda.memory_allocated() / 1024**2
            print(f"Memory allocated before: {mem_before:.2f} MB")

        tts = Qwen3TTSModel.from_pretrained(
            ckpt,
            device_map=device,
            dtype=dtype,
        )
        
        if torch.cuda.is_available():
            mem_after = torch.cuda.memory_allocated() / 1024**2
            print(f"Memory allocated after: {mem_after:.2f} MB")
            print(f"Memory reserved: {torch.cuda.memory_reserved() / 1024**2:.2f} MB")

        # Check model device
        model_device = next(tts.model.parameters()).device
        print(f"Main model device: {model_device}")
        
    except Exception as e:
        print(f"Error during model loading: {e}")

if __name__ == "__main__":
    check_model_device()
