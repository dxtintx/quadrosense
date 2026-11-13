namespace QuadroSense.ProjectWingman;

internal static class SelfTest
{
    public static void Run()
    {
        static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine($"PASS: {name}"); }
        var engine = new EffectEngine();
        var flight = new WingmanState(true, SpeedKmh: 800);
        Check(engine.Compose(new(), 0).Lighting.LightbarColor == new Color(0, 0, 0), "menu off");
        Check(engine.Compose(flight, 33).Lighting.LightbarColor == new Color(0, 255, 0), "flight green");
        Check(engine.Compose(flight with { TargetLocked = true }, 66).Lighting.LightbarColor == new Color(255, 128, 0), "lock orange");
        Check(engine.Compose(flight with { TargetLocked = true, MissileWarning = true }, 99).Lighting.LightbarColor == new Color(255, 0, 0), "warning overrides lock");
        var hit = engine.Compose(flight with { Health = .5f }, 132);
        Check(hit.Haptics.RumbleLow == 150, "HP loss vibration");
        Check(engine.Compose(flight with { Health = .5f }, 242).Lighting.LightbarColor.R == 0, "damage blink off");
        Check(engine.Compose(flight with { Health = .5f }, 342).Lighting.LightbarColor.R > 0, "damage blink on");
        var recovered = engine.Compose(flight with { Health = .5f }, 800);
        Check(recovered.Lighting.LightbarColor == new Color(0, 128, 0) && recovered.Haptics.RumbleLow == 0, "hit ends, HP scales brightness");
        for (var time = 833; time < 4000; time += 33) engine.Compose(flight with { GForce = 9 }, time);
        Check(engine.Compose(flight with { GForce = 9 }, 4000).Lighting.LightbarColor.G < 100, "G force dims gradually");
        var menu = engine.Compose(new(), 4033);
        Check(menu.LeftTrigger.Mode == "Off" && menu.RightTrigger.Mode == "Off" && menu.Haptics.RumbleLow == 0, "menu resets output");
        var takeoffTriggers = new EffectEngine().Compose(flight with { SpeedKmh = 0 }, 0);
        var fastTriggers = new EffectEngine().Compose(flight with { SpeedKmh = 1852 }, 0);
        Check(takeoffTriggers.LeftTrigger.Strength == 1 && takeoffTriggers.RightTrigger.Strength == 8, "SPD 0: brake 1, gas 8");
        Check(fastTriggers.LeftTrigger.Strength == 8 && fastTriggers.RightTrigger.Strength == 1, "SPD 1000: brake 8, gas 1");
        var midTriggers = new EffectEngine().Compose(flight with { SpeedKmh = 926 }, 0);
        Check(midTriggers.LeftTrigger.Strength == 4 && midTriggers.RightTrigger.Strength == 4, "SPD 500: balanced half resistance");
        var aboveMax = new EffectEngine().Compose(flight with { SpeedKmh = 2222.4f }, 0);
        Check(aboveMax.LeftTrigger == fastTriggers.LeftTrigger && aboveMax.RightTrigger == fastTriggers.RightTrigger, "SPD above 1000: clamp resistance");
        var cruise = new EffectEngine();
        var slow = cruise.Compose(flight with { SpeedKmh = 300 }, 0);
        Check(slow.Haptics.RumbleLow == 0 && slow.Haptics.RumbleHigh == 0, "low-speed rumble stops without throttle");
        var takeoffRumble = new EffectEngine().Compose(flight with { SpeedKmh = 300, Throttle = 1 }, 0);
        Check(takeoffRumble.Haptics.RumbleLow is > 0 and <= 18 && takeoffRumble.Haptics.RumbleHigh <= 5, "light low-speed vibration with throttle");
        Check(cruise.Compose(flight, 33).Haptics.RumbleLow == 0, "low-speed texture fades at cruise");
        foreach (var speed in new[] { 350f, 351, 400, 500, 650, 699, 700 })
        {
            var quiet = new EffectEngine().Compose(flight with { SpeedKmh = speed * 1.852f, Throttle = 1 }, 0);
            Check(quiet.Haptics.RumbleLow == 0 && quiet.Haptics.RumbleHigh == 0, $"no throttle vibration at HUD SPD {speed}");
        }
        var takeoff = new EffectEngine().Compose(flight with { SpeedKmh = 150, Throttle = 1 }, 0);
        Check(takeoff.Haptics.RumbleLow is > 0 and <= 18, "takeoff stays light even at full throttle");
        var fast = new EffectEngine().Compose(flight with { SpeedKmh = 1852, Throttle = 1 }, 0);
        var rising = new EffectEngine().Compose(flight with { SpeedKmh = 1389, Throttle = 1 }, 0);
        Check(fast.Haptics.RumbleLow > rising.Haptics.RumbleLow && rising.Haptics.RumbleLow > 0, "throttle rumble grows above HUD SPD 700");
        Check(new EffectEngine().Compose(flight with { SpeedKmh = 926, Throttle = 1 }, 0).Haptics == new Rumble(), "live calibration: 926 km/h is quiet at SPD 500 with full throttle");
        Check(new EffectEngine().Compose(flight with { SpeedKmh = 1000 }, 0).Haptics.RumbleLow == 0, "high-speed engine vibration requires throttle");
        var quietBrake = new EffectEngine();
        quietBrake.Compose(flight with { SpeedKmh = 600 }, 0);
        Check(quietBrake.Compose(flight with { SpeedKmh = 580, Brake = 1 }, 100).Haptics.RumbleLow > 100, "hard braking remains active in quiet speed band");
        Check(new EffectEngine().Compose(flight with { SpeedKmh = 500, GForce = 9 }, 0).Haptics.RumbleHigh > 0, "G force remains active in quiet speed band");
        var braking = new EffectEngine();
        braking.Compose(flight, 0);
        var heavy = braking.Compose(flight with { SpeedKmh = 780, Brake = 1 }, 100);
        Check(heavy.Haptics.RumbleLow > 100 && heavy.Haptics.RumbleHigh > 40, "hard deceleration produces heavy rumble");
        for (var time = 200; time <= 2100; time += 100) braking.Compose(flight with { SpeedKmh = 780, Brake = 1 }, time);
        Check(braking.Compose(flight with { SpeedKmh = 780, Brake = 1 }, 2200).Haptics.RumbleLow < 5, "held brake at constant speed releases rumble");
        var unbraked = new EffectEngine();
        unbraked.Compose(flight, 0);
        Check(unbraked.Compose(flight with { SpeedKmh = 780 }, 100).Haptics.RumbleLow == 0, "speed loss without brake does not trigger braking rumble");
        var resume = new EffectEngine();
        resume.Compose(flight, 0);
        resume.Compose(new(), 100);
        Check(resume.Compose(flight with { SpeedKmh = 700, Brake = 1 }, 200).Haptics.RumbleLow == 0, "resume does not produce false braking impact");
        Check(resume.Compose(flight with { SpeedKmh = 660, Brake = 1 }, 2200).Haptics.RumbleLow == 0, "long telemetry gap does not produce false braking impact");
    }
}
