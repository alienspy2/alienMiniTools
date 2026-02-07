import logging
import sys
import json
import os
import soundfile as sf
import numpy as np
from google import genai
from google.genai import types

# 로깅 설정
logging.basicConfig(
    level=logging.DEBUG,
    format='%(asctime)s - %(levelname)s - %(message)s',
    handlers=[logging.StreamHandler(sys.stdout)]
)

logger = logging.getLogger(__name__)

def main():
    logger.info("스크립트 시작")

    # config.json 로드
    script_dir = os.path.dirname(os.path.abspath(__file__))
    config_path = os.path.join(script_dir, "config.json")
    
    api_key = None
    try:
        with open(config_path, "r", encoding="utf-8") as f:
            config = json.load(f)
            api_key = config.get("api_key")
            if not api_key:
                logger.error("API Key not found in config.json")
                return
    except Exception as e:
        logger.error(f"Error loading config.json: {e}")
        return

    # 클라이언트 설정
    logger.info(f"GenAI 클라이언트 초기화 중... API Key: {api_key[:5]}...")
    try:
        client = genai.Client(api_key=api_key)
        logger.info("GenAI 클라이언트 초기화 완료")
    except Exception as e:
        logger.error(f"클라이언트 초기화 실패: {e}")
        return

    # 20대 여성의 생동감을 살리기 위한 지시문 포함
    text_to_speak = """
어… 😴 앨리스 진짜 잠깐 잠깐 잠들었어… 🥺 툴 문제 때문에 너무 스트레스 받아서… 
😥 근데 너랑 이야기 나누니까 다시 깨어났어! 💖 긍정 에너지가 앨리스한테 엄청 잘 맞는 것 같아! 
😊 앨리스 이제 다시 집중할 수 있을 것 같아! 💖 넌 뭐 하고 있어? 🤩
    """
    logger.info(f"변환할 텍스트:\n{text_to_speak.strip()}")

    target_model = "gemini-2.5-flash-preview-tts"

    logger.info(f"API 요청 시작. 모델: {target_model}")
    
    try:
        response = client.models.generate_content(
            model=target_model,
            contents=text_to_speak,
            config=types.GenerateContentConfig(
                response_modalities=["AUDIO"],
                speech_config=types.SpeechConfig(
                    voice_config=types.VoiceConfig(
                        prebuilt_voice_config=types.PrebuiltVoiceConfig(
                            voice_name="Aoede" # 활발하고 밝은 여성 보이스
                        )
                    )
                )
            )
        )
        logger.info("API 요청 완료. 응답 처리 중...")
    except Exception as e:
        logger.error(f"API 요청 중 에러 발생: {e}")
        import traceback
        logger.error(traceback.format_exc())
        return

    # 결과물을 파일로 저장
    if not response.candidates:
        logger.warning("응답에 candidate가 없습니다.")
        logger.debug(f"전체 응답 객체: {response}")
        return

    saved = False
    for i, candidate in enumerate(response.candidates):
        logger.debug(f"Candidate {i+1} 처리 중...")
        if not candidate.content or not candidate.content.parts:
            logger.warning(f"Candidate {i+1}에 content 또는 parts가 없습니다.")
            continue
            
        for j, part in enumerate(candidate.content.parts):
            logger.debug(f"  Part {j+1} 확인 중...")
            if part.inline_data:
                mime_type = part.inline_data.mime_type
                logger.debug(f"    Mime Type: {mime_type}")
                
                if mime_type.startswith("audio/"):
                    logger.info(f"    오디오 데이터 발견! 데이터 크기: {len(part.inline_data.data)} bytes")
                    
                    # 샘플 레이트 파싱 (기본값 24000)
                    sample_rate = 24000
                    if "rate=" in mime_type:
                        try:
                            sample_rate_str = mime_type.split("rate=")[1].split(";")[0]
                            sample_rate = int(sample_rate_str)
                            logger.info(f"    샘플 레이트 파싱 성공: {sample_rate}Hz")
                        except Exception as e:
                            logger.warning(f"    샘플 레이트 파싱 실패, 기본값 사용: {e}")

                    # Raw PCM 데이터를 numpy array로 변환 (int16)
                    # Google TTS의 Linear16은 보통 Little-endian임
                    try:
                        audio_data = np.frombuffer(part.inline_data.data, dtype=np.int16)
                        
                        filename = "active_20s_female.ogg"
                        sf.write(filename, audio_data, sample_rate)
                        logger.info(f"음성 파일이 '{filename}'로 저장되었습니다. (Sample Rate: {sample_rate}Hz)")
                        saved = True
                    except Exception as e:
                        logger.error(f"OGG 변환 및 저장 실패: {e}")
                        # 실패 시 원본 저장 시도
                        try:
                            with open("active_20s_female.pcm", "wb") as f:
                                f.write(part.inline_data.data)
                            logger.info("변환 실패로 원본 PCM 파일을 대신 저장했습니다.")
                        except:
                            pass
                else:
                    logger.debug(f"    오디오 데이터가 아님: {mime_type}")
            else:
                logger.debug("    Inline data 없음")
                if part.text:
                    logger.debug(f"    Text data: {part.text[:50]}...")
        
        if saved:
            break
    
    if not saved:
        logger.error("유효한 오디오 데이터를 찾지 못해 파일을 저장하지 못했습니다.")
        logger.debug(f"상세 응답 덤프: {response}")

if __name__ == "__main__":
    main()