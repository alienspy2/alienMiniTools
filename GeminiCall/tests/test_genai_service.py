import pytest
from unittest.mock import MagicMock, patch
from genai_service import GenAIService
from google.genai import types

@pytest.fixture
def mock_genai_client():
    with patch('genai_service.genai.Client') as mock_client_cls:
        client_inst = MagicMock()
        mock_client_cls.return_value = client_inst
        yield client_inst

@pytest.mark.asyncio
async def test_convert_messages(mock_genai_client):
    service = GenAIService(api_key="TEST_KEY", default_model="gemini-test")
    ollama_msgs = [
        {'role': 'system', 'content': 'Be nice'},
        {'role': 'user', 'content': 'Hi'},
        {'role': 'assistant', 'content': 'Hello'}
    ]

    contents, sys_inst = service._convert_messages(ollama_msgs, "gemini-test")

    assert sys_inst == 'Be nice'
    assert len(contents) == 2
    assert contents[0].role == 'user'
    assert contents[0].parts[0].text == 'Hi'
    assert contents[1].role == 'model' # Converted from assistant

@pytest.mark.asyncio
async def test_generate_response_calls_client(mock_genai_client):
    service = GenAIService(api_key="TEST_KEY", default_model="gemini-test")

    # Setup mock return
    mock_response = MagicMock()
    mock_response.text = "Generated Reply"
    mock_genai_client.models.generate_content.return_value = mock_response

    response_text = await service.generate_response(
        model_name="gemini-test",
        messages=[{'role': 'user', 'content': 'Hello'}]
    )

    assert response_text == "Generated Reply"
    mock_genai_client.models.generate_content.assert_called_once()

    # Check arguments
    call_kwargs = mock_genai_client.models.generate_content.call_args.kwargs
    assert call_kwargs['model'] == "gemini-test"
    assert len(call_kwargs['contents']) == 1

@pytest.mark.asyncio
async def test_get_provider_name(mock_genai_client):
    service = GenAIService(api_key="TEST_KEY", default_model="gemini-test")
    assert service.get_provider_name() == "gemini"
