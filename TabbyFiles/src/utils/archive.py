import subprocess
import shutil


def compress_7z(sources, output_path):
    """파일/폴더 목록을 7z로 압축합니다.

    Args:
        sources: 압축할 파일/폴더 경로 리스트
        output_path: 출력 .7z 파일 경로

    Returns:
        (success: bool, message: str)
    """
    sz = shutil.which("7z")
    if not sz:
        return False, "7z가 설치되어 있지 않습니다."

    cmd = [sz, "a", output_path] + sources
    print(f"[압축] 명령: {' '.join(cmd)}")
    try:
        result = subprocess.run(cmd, capture_output=True, text=True, timeout=300)
        if result.returncode == 0:
            print(f"[압축] 성공: {output_path}")
            return True, output_path
        else:
            print(f"[압축] 실패: {result.stderr}")
            return False, result.stderr
    except subprocess.TimeoutExpired:
        return False, "압축 시간 초과 (5분)"
    except Exception as e:
        print(f"[압축] 예외: {e}")
        return False, str(e)
