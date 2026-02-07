---
name: aca-plan
description: Provide a topic to create a detailed execution plan and save it as an appropriate_name_plan.md file. Use when you need to structure a new project or task.
---

# ACA Plan Generator

사용자가 제공한 주제에 대해 포괄적이고 체계적인 계획을 수립하고, 이를 적절한 이름의 마크다운 파일로 저장하는 스킬입니다.

## 절차 (Procedure)

1.  **주제 분석 (Understand the Topic)**:
    *   사용자의 요청을 분석하여 범위와 목표를 파악합니다.
    *   **중요:** 사용자의 의도나 요구사항이 완전히 명확해질 때까지, 계획을 수립하기 전에 적극적으로 추가 질문을 하여 구체적인 정보를 수집합니다. (예: 플랫폼, 언어, 주요 기능 등)

2.  **계획 수립 (Devise a Plan)**:
    *   전체 과정을 단계(Phase)별로 나누어 상세 실행 계획을 작성합니다.
    *   **중요:** 구현 계획과 동시에 **테스트 플랜(Test Plan)**을 반드시 수립해야 합니다.
    *   **중요:** 계획(Plan) 파일이 작성된 후, 반드시 **사용자의 검수 및 승인**을 받아야 합니다 within the plan file. 사용자가 명시적으로 계획을 승인하기 전까지는 절대 구현(코딩)을 시작하지 마십시오.
    *   각 Phase의 구현이 완료될 때마다 수행해야 할 **필수 테스트 절차**를 명시하십시오. **주의:** 테스트는 사용자가 수행하는 것이 아니라, **Agent가 직접 코드를 실행하거나 도구를 사용하여 수행하고 검증**해야 합니다.
    *   다음 내용을 포함해야 합니다:
        *   **목표 (Objective)**: 성공 기준에 대한 명확한 정의.
        *   **단계 (Phases)**: 주요 개발 단계 구분.
        *   **작업 (Tasks)**: 각 단계별 구체적인 구현 작업.
        *   **테스트 (Tests)**: 각 단계 완료 시 **Agent가 직접 수행할** 테스트 항목 및 검증 방법.
        *   **리소스 (Resources)**: 필요한 도구, 라이브러리, 또는 자산.
        *   **타임라인 (Timeline - 선택)**: 시간 제약이 언급된 경우 포함.

3.  **진행 상황 추적 (Track Progress)**:
    *   계획 파일 내에 **진행 상황(Progress Tracking)** 섹션을 반드시 포함해야 합니다.
    *   각 작업 항목 옆에 체크박스(`- [ ]`)를 사용하여 완료 여부를 표시할 수 있도록 작성하십시오.
    *   **중요 (Critical):** Agent는 작업이 완료되거나 테스트가 통과할 때마다 **사용자의 별도 요청 없이 자동으로** 이 계획 파일을 열어 해당 항목을 체크(`- [x]`)하고 진행 상황을 최신 상태로 유지해야 합니다. 이것은 Agent의 의무입니다.

4.  **파일명 결정 (Determine Filename)**:
    *   주제를 기반으로 영문 소문자와 언더스코어(_)를 사용한 snake_case 형식의 파일명을 만듭니다.
    *   파일명 끝에 `_plan.md`를 붙입니다.
    *   예시: 주제 "Node.js 웹 서버 구축" -> `build_nodejs_web_server_plan.md`

4.  **계획 저장 (Save the Plan)**:
    *   결정된 파일명으로 현재 작업 디렉토리에 내용을 작성합니다.
    *   파일 생성을 사용자에게 확인받습니다.

## 사용 예시 (Example Usage)

사용자: "제품 런칭을 위한 마케팅 캠페인 계획 세워줘"
실행:
1.  요청 분석.
2.  상세 마케팅 계획 수립.
3.  파일명 결정: `marketing_campaign_plan.md`
4.  파일에 내용 저장.
