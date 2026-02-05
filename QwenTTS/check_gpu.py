import sys
import platform
import subprocess

print(f'Python version: {sys.version}')

try:
    import torch
    print(f'PyTorch version: {torch.__version__}')
    print(f'CUDA version: {torch.version.cuda}')
    if torch.cuda.is_available():
        print(f'GPU Name: {torch.cuda.get_device_name(0)}')
        cap = torch.cuda.get_device_capability(0)
        print(f'Compute Capability: {cap[0]}.{cap[1]}')
    else:
        print('CUDA is not available in PyTorch')
except ImportError:
    print('PyTorch is not installed.')

# Fallback GPU check
try:
    print('\nSystem GPU Check (nvidia-smi):')
    output = subprocess.check_output(['nvidia-smi', '--query-gpu=name,compute_cap', '--format=csv,noheader'], encoding='utf-8')
    print(output.strip())
except Exception as e:
    print(f'Could not run nvidia-smi: {e}')
