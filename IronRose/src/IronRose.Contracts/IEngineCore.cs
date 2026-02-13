namespace IronRose.Contracts
{
    /// <summary>
    /// 엔진 코어 인터페이스
    /// Bootstrapper가 엔진과 통신하기 위한 계약
    /// </summary>
    public interface IEngineCore
    {
        /// <summary>
        /// 엔진 초기화 (윈도우, 그래픽 디바이스 등)
        /// </summary>
        void Initialize();

        /// <summary>
        /// 윈도우 이벤트 처리
        /// </summary>
        /// <returns>윈도우가 여전히 열려있으면 true</returns>
        bool ProcessEvents();

        /// <summary>
        /// 게임 로직 업데이트 (GameObject, Component 등)
        /// </summary>
        /// <param name="deltaTime">프레임 간 시간 (초)</param>
        void Update(double deltaTime);

        /// <summary>
        /// 렌더링 수행
        /// </summary>
        void Render();

        /// <summary>
        /// 엔진 종료 및 리소스 해제
        /// </summary>
        void Shutdown();
    }
}
