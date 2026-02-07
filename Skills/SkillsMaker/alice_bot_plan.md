# Alice Bot 구현 계획

## 1. 목표 (Objective)
- Ollama를 백엔드로 사용하는 로컬 챗봇 'Alice' 개발.
- 사용자 정의 페르소나(Alice) 적용.
- 스마트한 대화 기록 관리: 대화 내역을 첨부하되, 1000자를 초과할 경우 Ollama를 사용하여 자동으로 요약, 컨텍스트 윈도우 효율화.

## 2. 기술 스택 (Tech Stack)
- **Language**: Python 3.x
- **Core Engine**: Custom LLM Server (http://localhost:20006)
- **Target Model**: gemma-3-27b-it
- **Libraries**: 
  - `requests` (API 통신)
  - `colorama` (선택: CLI 가독성 향상)

## 3. 단계별 계획 (Phases)

### Phase 1: 환경 설정 및 기본 연결 (Environment & Basic Setup)
- [x] 프로젝트 구조 생성 및 가상환경(`venv`) 설정.
- [x] 필수 패키지 설치 (`pip install requests`).
- [ ] 로컬 API 서버(20006) 연결 확인 및 테스트 스크립트 작성.

### Phase 2: 기본 채팅 및 페르소나 구현 (Basic Chat & Persona)
- [ ] **System Prompt** 정의: 'Alice'라는 이름과 성격(페르소나) 부여.
- [ ] 기본 채팅 루프(Loop) 구현
    - 사용자 입력 수신
    - 메시지 리스트(Context) 구성
    - API 호출(requests) 및 응답 출력
- [ ] 페르소나 작동 테스트: 자기소개 요청 시 Alice로 응답 확인.

### Phase 3: 메모리 관리 시스템 - 핵심 기능 (Memory & Summarization)
- [ ] **Chat History 관리 클래스** 설계.
    - 대화 내용(User/Assistant) 저장소 구현.
- [ ] **자동 요약 로직** 구현.
    - 매 턴마다 현재 History의 길이(글자 수) 체크.
    - 1000자 초과 시:
        1. 별도의 API 호출로 현재까지의 내용 요약 요청.
        2. 기존 상세 History를 축소(오래된 것 삭제)하고 '요약된 문맥'을 상단에 삽입.
        3. 최신 대화(최근 N턴)는 상세 유지.

### Phase 4: 고도화 및 인터페이스 (Refinement & UI)
- [ ] CLI 인터페이스 다듬기 (User/Alice 구분 표시).
- [ ] (선택사항) 로그 출력: 요약이 일어나는 시점을 사용자가 알 수 있도록 디버그 모드 추가.
- [ ] 예외 처리: API 서버 미응답 시 재시도 또는 에러 메시지.

## 4. 테스트 계획 (Test Plan)
- **TC-01 [연결]**: Python 스크립트에서 API 서버(gemma-3-27b-it)와 통신 성공.
- **TC-02 [페르소나]**: 챗봇이 지시에 따라 "Alice"로서 대화하는지 검증.
- **TC-03 [기억/요약]**: 
    - 의도적으로 긴 대화(1000자 이상) 입력.
    - 시스템 로그를 통해 '요약 트리거'가 발동하는지 확인.
    - 요약 후에도 이전 대화의 맥락(Context)을 기억하고 답변하는지 확인.

## 5. 진행 상황 (Progress Tracking)
- 진행 중인 작업은 `[ ]`를 `[x]`로 변경하여 표시하세요.
