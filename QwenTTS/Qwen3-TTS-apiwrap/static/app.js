/**
 * Qwen3-TTS Web Chat - Frontend Logic
 */

// Configuration
const CONFIG = {
    apiEndpoint: '/api/tts',
    defaultLanguage: 'Korean',
    defaultSpeaker: 'Sohee',
};

// State
const state = {
    isLoading: false,
    audioContext: null,
};

// DOM Elements
const elements = {
    messages: null,
    textInput: null,
    sendBtn: null,
    languageSelect: null,
    speakerSelect: null,
    statusIndicator: null,
};

/**
 * Initialize the application
 */
function init() {
    // Get DOM elements
    elements.messages = document.getElementById('messages');
    elements.textInput = document.getElementById('textInput');
    elements.sendBtn = document.getElementById('sendBtn');
    elements.languageSelect = document.getElementById('languageSelect');
    elements.speakerSelect = document.getElementById('speakerSelect');
    elements.statusIndicator = document.getElementById('statusIndicator');

    // Event listeners
    elements.sendBtn.addEventListener('click', handleSend);
    elements.textInput.addEventListener('keydown', handleKeyDown);
    elements.textInput.addEventListener('input', autoResize);

    // Check server health
    checkHealth();

    // Focus input
    elements.textInput.focus();
}

/**
 * Check server health
 */
async function checkHealth() {
    try {
        const response = await fetch('/api/health');
        const data = await response.json();

        if (data.status === 'ok' && data.tts_connected) {
            setStatus('connected', 'Connected');
        } else {
            setStatus('error', 'TTS Disconnected');
        }
    } catch (error) {
        setStatus('error', 'Server Error');
        console.error('Health check failed:', error);
    }
}

/**
 * Set status indicator
 */
function setStatus(type, text) {
    const indicator = elements.statusIndicator;
    indicator.className = 'header-status' + (type === 'error' ? ' error' : '');
    indicator.innerHTML = `<span class="status-dot"></span>${text}`;
}

/**
 * Handle send button click
 */
async function handleSend() {
    const text = elements.textInput.value.trim();
    if (!text || state.isLoading) return;

    const language = elements.languageSelect.value;
    const speaker = elements.speakerSelect.value;

    // Clear input
    elements.textInput.value = '';
    autoResize();

    // Add user message
    addMessage('user', text);

    // Add loading message
    const loadingId = addLoadingMessage();

    // Set loading state
    setLoading(true);

    try {
        // Call TTS API
        const response = await fetch(CONFIG.apiEndpoint, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify({
                text,
                language,
                speaker,
            }),
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        const data = await response.json();

        // Remove loading message
        removeMessage(loadingId);

        // Add audio message
        addAudioMessage(data.audio_base64, data.sample_rate);

        // Auto-play audio
        playAudio(data.audio_base64);

    } catch (error) {
        console.error('TTS failed:', error);

        // Remove loading message
        removeMessage(loadingId);

        // Add error message
        addMessage('assistant', `Error: ${error.message}`);
    } finally {
        setLoading(false);
    }
}

/**
 * Handle keyboard events
 */
function handleKeyDown(event) {
    if (event.key === 'Enter' && !event.shiftKey) {
        event.preventDefault();
        handleSend();
    }
}

/**
 * Auto-resize textarea
 */
function autoResize() {
    const textarea = elements.textInput;
    textarea.style.height = 'auto';
    textarea.style.height = Math.min(textarea.scrollHeight, 120) + 'px';
}

/**
 * Set loading state
 */
function setLoading(loading) {
    state.isLoading = loading;
    elements.sendBtn.disabled = loading;
    elements.textInput.disabled = loading;
}

/**
 * Add a message to the chat
 */
function addMessage(type, content) {
    const messageId = 'msg-' + Date.now();
    const messageEl = document.createElement('div');
    messageEl.id = messageId;
    messageEl.className = `message ${type}`;
    messageEl.innerHTML = `
        <div class="message-content">${escapeHtml(content)}</div>
    `;
    elements.messages.appendChild(messageEl);
    scrollToBottom();
    return messageId;
}

/**
 * Add loading message
 */
function addLoadingMessage() {
    const messageId = 'msg-loading-' + Date.now();
    const messageEl = document.createElement('div');
    messageEl.id = messageId;
    messageEl.className = 'message assistant loading';
    messageEl.innerHTML = `
        <div class="message-content">
            <div class="loading-dots">
                <span></span>
                <span></span>
                <span></span>
            </div>
            Generating audio...
        </div>
    `;
    elements.messages.appendChild(messageEl);
    scrollToBottom();
    return messageId;
}

/**
 * Add audio message
 */
function addAudioMessage(base64Audio, sampleRate) {
    const messageId = 'msg-audio-' + Date.now();
    const messageEl = document.createElement('div');
    messageEl.id = messageId;
    messageEl.className = 'message assistant';
    messageEl.innerHTML = `
        <div class="message-audio">
            <button class="audio-play-btn" onclick="playAudio('${base64Audio}')" title="Play">
                ▶
            </button>
            <div class="audio-info">
                <strong>Audio Generated</strong>
                <span>${sampleRate} Hz • WAV</span>
            </div>
        </div>
    `;
    elements.messages.appendChild(messageEl);
    scrollToBottom();
    return messageId;
}

/**
 * Remove a message by ID
 */
function removeMessage(messageId) {
    const messageEl = document.getElementById(messageId);
    if (messageEl) {
        messageEl.remove();
    }
}

/**
 * Play audio from base64
 */
async function playAudio(base64Audio) {
    try {
        // Decode base64 to ArrayBuffer
        const binaryString = atob(base64Audio);
        const bytes = new Uint8Array(binaryString.length);
        for (let i = 0; i < binaryString.length; i++) {
            bytes[i] = binaryString.charCodeAt(i);
        }

        // Create audio context if needed
        if (!state.audioContext) {
            state.audioContext = new (window.AudioContext || window.webkitAudioContext)();
        }

        // Decode audio data
        const audioBuffer = await state.audioContext.decodeAudioData(bytes.buffer);

        // Create source and play
        const source = state.audioContext.createBufferSource();
        source.buffer = audioBuffer;
        source.connect(state.audioContext.destination);
        source.start(0);

    } catch (error) {
        console.error('Audio playback failed:', error);

        // Fallback: use HTML5 Audio
        try {
            const audio = new Audio('data:audio/wav;base64,' + base64Audio);
            audio.play();
        } catch (e) {
            console.error('Fallback audio also failed:', e);
        }
    }
}

/**
 * Scroll chat to bottom
 */
function scrollToBottom() {
    elements.messages.scrollTop = elements.messages.scrollHeight;
}

/**
 * Escape HTML characters
 */
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// Initialize on DOM ready
document.addEventListener('DOMContentLoaded', init);
