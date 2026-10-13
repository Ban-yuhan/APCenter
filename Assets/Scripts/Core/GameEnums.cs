using System;

namespace APCenter.Core
{
    public enum PackageDestination
    {
        None = 0,
        Seoul = 1,   // 빨간색 (수도권)
        Busan = 2,   // 파란색 (영남권)
        Daegu = 3    // 초록색 (호남/충청권)
    }

    public enum PackageState
    {
        Unloaded,       // 입고되어 하차 대기장 바닥에 있음
        Carried,        // 플레이어나 직원이 들고 이동 중
        Sorted,         // 해당 목적지 분류대에 분류 완료됨
        LoadedInTruck,  // 출고 트럭 내부 격자에 적재됨
        ReturnPending,  // 오분류 또는 불량으로 반품대 보관 중
        Scrapped,       // 불량 판정으로 폐기됨
        ReInbounded     // 검수 완료되어 정상 재입고됨
    }

    public enum WorkerRole
    {
        Unload = 0,     // 상하차 특화
        Sort = 1,       // 분류 특화
        Transport = 2,  // 운반 특화
        Load = 3,       // 적재 특화
        Inspection = 4  // 검수 특화
    }

    public enum WorkerRank
    {
        Rookie = 0,     // 신입 (정확도 60%, 시급 100)
        Junior = 1,     // 일반 (정확도 80%, 시급 160)
        Senior = 2,     // 숙련 (정확도 95%, 시급 240)
        Veteran = 3     // 베테랑 (정확도 100%, 시급 350)
    }

    public enum WorkerType
    {
        Normal,         // 일반 정규 알바
        Temporary       // 단기알바/대타 (속도 1.8배, 스택 2배, 시급 3배)
    }

    public enum FacilityTier
    {
        SmallHub = 1,     // Tier 1: 작은 창고 (손수레 카트, 알바 3명)
        MediumCenter = 2, // Tier 2: 중형 센터 (전동카트, 컨베이어 보조, 알바 6명)
        MegaHub = 3       // Tier 3: 메가 허브 (지게차, 자동분류기, 알바 12명)
    }

    public enum GameEventType
    {
        None,
        FeverTime,          // [앙팡데이]: 1분간 주문량 2배, 수익 2배
        UrgentDelivery,     // 긴급배송: VIP 특송 (제한시간 60초)
        BadWeather_Snow,    // ④ [기상 - 눈]: 외부 통로 제설 발판 (이탈 시 감소) 100% 완료 미션
        BadWeather_Rain,    // ② [기상 - 비]: 입/출고 도크 옆 차수판 발판 (이탈 시 유지) 100% 완료 미션
        BadWeather_Typhoon, // ③ [기상 - 태풍]: 창문 앞 창문보강 발판 (이탈 시 유지) 100% 완료 미션
        BadWeather_Blackout,// ① [정전]: 발전기 발판 (이탈 시 감소) 100% 완료 미션
        CorporateOrder      // ⑤ [기업 대량주문]: 목표 수량(0/30), 1분 내 출고 완료 시 성공
    }
}
