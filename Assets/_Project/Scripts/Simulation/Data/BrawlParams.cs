namespace Knockbox.Simulation
{
    /// <summary>
    /// CR-004 실험 경기의 수치 덮어쓰기(S-160·S-161). 튜닝 에셋 값 위에 얹는다. 실행기와 테스트가 같은 값을 쓴다.
    /// </summary>
    public static class BrawlParams
    {
        /// <summary>D/E 피드백: 캐릭터 크기 20% 축소(몸 0.9 → 0.72). 판정(주먹 원, 잡기 상자, 붙잡기 거리)도 Combat에서 같은 비율로 줄인다.</summary>
        public const float BodyScale = 0.8f;

        public static MovementParams Movement(MovementParams movement)
        {
            movement.BodySize *= BodyScale;
            return movement;
        }

        static HitCircle Scale(HitCircle c) => new HitCircle(c.Start * BodyScale, c.Mid * BodyScale, c.End * BodyScale, c.Radius * BodyScale);

        /// <summary>KO 60틱 + 카운트다운 30틱 ≈ KO 뒤 1.5초에 다음 라운드, 3선승(9절 기본값).</summary>
        public static MatchParams Match(MatchParams match)
        {
            match.CountdownTicks = 30;
            match.KoTicks = 60;
            match.RoundsToWin = 3;
            return match;
        }

        /// <summary>
        /// 단계 A 피드백: 돌진이 느리고 길게 일직선으로 간다 → 더 빠르게 나가서 강하게 감속하고 일찍 끝난다(약 2.5~6칸).
        /// 지형에 부딪히면 반사 대신 멈추고 작은 반동만(고무벽 느낌 제거, 5.1 "벽에 닿음: 종료").
        /// </summary>
        /// <param name="surfaceMode">표면 이동(7절): 0 끔, 1 D, 2 E.</param>
        public static CombatParams Combat(CombatParams combat, int surfaceMode = 0, bool separateChargeInput = true,
            bool weaponsEnabled = false, bool combinedAttackInput = false, bool throwEnabled = false, bool revisedBalance = false)
        {
            combat.Charge.FullChargeTicks = 24;
            combat.Charge.LungeMinSpeed = 24f;
            combat.Charge.LungeMaxSpeed = 38f;
            combat.Charge.LungeDrag = 90f;
            combat.Charge.LungeEndSpeed = 12f;
            combat.Charge.LungeMaxTicks = 14;
            combat.Charge.LungeMaxBounces = 0;
            combat.Charge.LungeStopRecoil = 4f;
            combat.Charge.LungeStopKeep = 0.25f;
            combat.Body = BodyParams.Default;
            combat.Body.SeparateChargeInput = separateChargeInput && !combinedAttackInput;
            combat.Body.CombinedAttackInput = combinedAttackInput;
            // 현재 실행기는 명시적으로 켠다. 구 조작/물리 고정 자료는 기존 설정을 유지한다.
            combat.Weapons = weaponsEnabled ? BrawlWeaponParams.Default : default;
            if (weaponsEnabled && throwEnabled)
                combat.Weapons = BrawlWeaponParams.WithThrowing(combat.Weapons);
            // 피드백 2: 이전 v8 기록의 수치는 보존하고 현재 경기에서만 최소 충전·투척 속도를 조정한다.
            if (combinedAttackInput && revisedBalance)
            {
                combat.Body.MinLungeChargeTicks = 12; // 준비 8 + 충전 12 = 총 20틱(1/3초).
                if (combat.Weapons.ThrowEnabled)
                    combat.Weapons.ThrowSpeed = 22f;
            }
            combat.Body.Punch.Circle0 = Scale(combat.Body.Punch.Circle0);
            combat.Body.Punch.Circle1 = Scale(combat.Body.Punch.Circle1);
            // 단계 C(5.2): 마우스로 겨누고 클릭하기에 20틱은 짧다 → 60틱. 피드백 뒤로는 자동 던짐 대신 그때 놓아준다.
            combat.Grab.MaxHoldTicks = 60;
            // 단계 C 피드백: 잡기 판정이 패링처럼 빡빡하다 → 선딜 5 → 3, 활성 3 → 9, 상자 0.7×0.9 → 1.1×1.3(중심 0.65 → 0.75, 커서 방향).
            combat.Grab.StartupTicks = 3;
            combat.Grab.ActiveTicks = 9;
            combat.Grab.BoxOffset = 0.75f;
            combat.Grab.BoxWidth = 1.1f;
            combat.Grab.BoxHeight = 1.3f;
            combat.Grab.BoxOffset *= BodyScale;
            combat.Grab.BoxWidth *= BodyScale;
            combat.Grab.BoxHeight *= BodyScale;
            combat.Grab.HoldOffset *= BodyScale;
            combat.Grab.BackThrowOffset *= BodyScale;
            // 던지기는 일직선으로 거의 반대편까지: 속도 28 → 48, 던져진 상태 24 → 30틱(그동안 중력 0.2배).
            combat.Grab.ThrowSpeed = 48f;
            combat.Grab.ThrownTicks = 30;
            combat.Body.SurfaceMode = surfaceMode;
            // D/E 피드백 2: 벽 기어오르기(벽 쪽 + W, 달리기의 약 40%, 제한 없음)와 모서리 잡고 오르기.
            combat.Body.ClimbSpeed = 4.4f;
            return combat;
        }
    }
}
