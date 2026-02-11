from openai import OpenAI
from config_loader import load_config

config = load_config()
api_key = config['providers']['openai']['api_key']

client = OpenAI(api_key=api_key)
models = client.models.list()
for model in models:
    print(model.id)
    