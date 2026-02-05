import torch
print(f'Torhc: {torch.__version__}')
if torch.cuda.is_available():
    print('CUDA Available!')
    print(torch.rand(5).cuda())
else:
    print('CUDA NOT Available')
