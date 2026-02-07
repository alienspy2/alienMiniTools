
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
    format: Optional[Any] = None
    options: Optional[Dict[str, Any]] = None
    keep_alive: Optional[Union[int, str]] = None

class OllamaGenerateRequest(BaseModel):
    model: str
    prompt: str
    suffix: Optional[str] = None
    system: Optional[str] = None
    template: Optional[str] = None
    context: Optional[List[int]] = None
    stream: Optional[bool] = False
    raw: Optional[bool] = None
    format: Optional[Any] = None
    images: Optional[List[str]] = None
    options: Optional[Dict[str, Any]] = None
    keep_alive: Optional[Union[int, str]] = None

class OllamaChatResponse(BaseModel):
    model: str
    created_at: str
    message: ChatMessage
    done: bool
    done_reason: Optional[str] = None
    total_duration: Optional[int] = None
    load_duration: Optional[int] = None
    prompt_eval_count: Optional[int] = None
    prompt_eval_duration: Optional[int] = None
    eval_count: Optional[int] = None
    eval_duration: Optional[int] = None

class OllamaGenerateResponse(BaseModel):
    model: str
    created_at: str
    response: str
    done: bool
    done_reason: Optional[str] = None
    context: Optional[List[int]] = None
    total_duration: Optional[int] = None
    load_duration: Optional[int] = None
    prompt_eval_count: Optional[int] = None
    prompt_eval_duration: Optional[int] = None
    eval_count: Optional[int] = None
    eval_duration: Optional[int] = None

class OllamaModelDetails(BaseModel):
    parent_model: str = ""
    format: str = "gguf"
    family: str = "gemini"
    families: Optional[List[str]] = None
    parameter_size: str = ""
    quantization_level: str = ""

class OllamaModelInfo(BaseModel):
    name: str
    model: str
    modified_at: str
    size: int
    digest: str
    details: OllamaModelDetails = OllamaModelDetails()

class OllamaTagsResponse(BaseModel):
    models: List[OllamaModelInfo]

class OllamaVersionResponse(BaseModel):
    version: str

# /api/show
class OllamaShowRequest(BaseModel):
    model: str
    name: Optional[str] = None
    verbose: Optional[bool] = False

class OllamaShowResponse(BaseModel):
    modelfile: str = ""
    parameters: str = ""
    template: str = ""
    system: str = ""
    details: OllamaModelDetails = OllamaModelDetails()
    model_info: Dict[str, Any] = {}
    modified_at: Optional[str] = None

# /api/ps
class OllamaProcessModel(BaseModel):
    name: str
    model: str
    size: int
    digest: str
    details: OllamaModelDetails = OllamaModelDetails()
    expires_at: str
    size_vram: int = 0

class OllamaProcessResponse(BaseModel):
    models: List[OllamaProcessModel]

# --- JSON-RPC 2.0 Models ---
class RPCParams(BaseModel):
    model: Optional[str] = None
    messages: List[Dict[str, Any]]
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

# --- MCP Server Management Models ---
class McpServerAddRequest(BaseModel):
    name: str
    url: str

class McpServerAddResponse(BaseModel):
    name: str
    url: str
    message: str = "MCP server registered"

class McpServerRemoveRequest(BaseModel):
    name: str

class McpServerRemoveResponse(BaseModel):
    name: str
    message: str = "MCP server removed"

class McpServerInfo(BaseModel):
    name: str
    url: str

class McpServerListResponse(BaseModel):
    servers: List[McpServerInfo]

# --- MCP + Generate (JSON-RPC 2.0) Models ---
class McpRPCParams(BaseModel):
    model: Optional[str] = None
    messages: List[Dict[str, Any]]
    temperature: Optional[float] = None
    max_output_tokens: Optional[int] = None
    mcp_servers: Optional[List[str]] = None
    max_iterations: Optional[int] = 5

class McpRPCRequest(BaseModel):
    jsonrpc: str = "2.0"
    method: str
    params: McpRPCParams
    id: Union[int, str, None]
