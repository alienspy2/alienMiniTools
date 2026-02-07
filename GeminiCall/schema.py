
from pydantic import BaseModel, Field
from typing import List, Optional, Union, Any, Dict

# --- Shared Models ---
class ChatMessage(BaseModel):
    role: str
    content: str
    images: Optional[List[str]] = None
    tool_calls: Optional[List[Any]] = None

# --- Ollama API Models ---
class OllamaChatRequest(BaseModel):
    model: str
    messages: List[ChatMessage]
    stream: Optional[bool] = False
    format: Optional[str] = None
    options: Optional[Dict[str, Any]] = None # temperature, etc.
    keep_alive: Optional[Union[int, str]] = None

class OllamaGenerateRequest(BaseModel):
    model: str
    prompt: str
    system: Optional[str] = None
    stream: Optional[bool] = False
    options: Optional[Dict[str, Any]] = None
    keep_alive: Optional[Union[int, str]] = None

class OllamaChatResponse(BaseModel):
    model: str
    created_at: str
    message: ChatMessage
    done: bool
    total_duration: Optional[int] = None
    load_duration: Optional[int] = None
    prompt_eval_count: Optional[int] = None
    eval_count: Optional[int] = None
    eval_duration: Optional[int] = None

class OllamaGenerateResponse(BaseModel):
    model: str
    created_at: str
    response: str
    done: bool
    context: Optional[List[int]] = None
    total_duration: Optional[int] = None
    load_duration: Optional[int] = None
    prompt_eval_count: Optional[int] = None
    eval_count: Optional[int] = None
    eval_duration: Optional[int] = None

class OllamaModelInfo(BaseModel):
    name: str
    model: str
    modified_at: str
    size: int
    digest: str

class OllamaTagsResponse(BaseModel):
    models: List[OllamaModelInfo]

class OllamaVersionResponse(BaseModel):
    version: str

# --- JSON-RPC 2.0 Models ---
# Minimal RPC wrapper models since FastAPI handles body parsing mostly
class RPCParams(BaseModel):
    model: Optional[str] = None # Optional in RPC, if missing use default
    messages: List[Dict[str, Any]] # Flexible, key "parts" used in user plan.
    temperature: Optional[float] = None
    max_output_tokens: Optional[int] = None

class RPCRequest(BaseModel):
    jsonrpc: str = "2.0"
    method: str
    params: RPCParams
    id: Union[int, str, None]

class RPCResponse(BaseModel):
    jsonrpc: str = "2.0"
    result: Optional[Dict[str, Any]] = None
    error: Optional[Dict[str, Any]] = None
    id: Union[int, str, None]
