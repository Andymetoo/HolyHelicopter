using System;
using System.Reflection;
using UnityEngine;

namespace HolyHelicopter
{
    /// <summary>Command-driven handling checks. Teleport is used only to reset each independent scenario.</summary>
    public static class FlightValidation
    {
        const float Dt = 1f / 60f;

        public static void Run(Game game, Action<string, bool, string> report)
        {
            if (game == null || game.Flight == null) throw new ArgumentException("A live game and flight controller are required.");
            if (report == null) throw new ArgumentNullException(nameof(report));
            FlightController flight = game.Flight;
            Vector3 originalPosition = flight.transform.position;
            float originalHeading = flight.Heading, originalPitch = flight.LookPitch;
            float originalFuel = game.Fuel, originalMotion = flight.CameraMotion;
            FlightControlMode originalMode = flight.Mode;
            game.Fuel = 100;
            try
            {
                for (int mode = 0; mode < 2; mode++)
                {
                    flight.SetMode((FlightControlMode)mode);
                    string label = "Flight " + flight.ModeLabel + ": ";
                    Check(label + "quick progressive acceleration", report, () => CheckAcceleration(flight));
                    Check(label + "short braking and stable hover", report, () => CheckBraking(flight));
                    Check(label + "responsive reversal", report, () => CheckReversal(flight));
                    Check(label + "strafe and diagonal cruise", report, () => CheckStrafe(flight));
                    Check(label + "immediate yaw", report, () => mode == 0 ? CheckYaw(flight) : CheckBoundedYaw(flight));
                    Check(label + "terrain clearance", report, () => CheckTerrain(flight));
                    Check(label + "altitude hold, climb and scroll", report, () => CheckAltitude(flight));
                    Check(label + "stable sight during banking", report, () => CheckStableAim(flight));
                    Check(label + "frame rate independence", report, () => CheckFrameRates(flight));
                    Check(label + "ground aim and actual weapon impact", report, () => CheckWeaponAim(game));
                    Check(label + "circle a stationary target", report, () => CheckOrbit(flight));
                    Check(label + "low supply approach and stop", report, () => CheckSupplyApproach(game));
                    Check(label + "no managed allocation in flight tick", report, () => CheckAllocations(flight));
                }
                flight.SetMode(FlightControlMode.Direct);
                Check("Flight A: turn retains moderate world momentum", report, () => CheckTurningMomentum(flight));
                Check("Flight: mode handoff preserves camera, sight and velocity", report, () => CheckModeSwitch(flight));
                Check("Flight B: chart/menu mouse ownership", report, () => CheckInputOwnership(game));
            }
            finally
            {
                flight.CameraMotion = originalMotion;
                game.Fuel = originalFuel;
                flight.SetMode(originalMode);
                flight.Teleport(originalPosition, originalHeading, originalPitch);
            }
        }

        static void Check(string name, Action<string, bool, string> report, Func<string> run)
        {
            bool passed = true;
            string detail;
            try { detail = run(); }
            catch (Exception exception) { passed = false; detail = exception.Message; }
            report(name, passed, detail);
        }

        static void Reset(FlightController flight, float heading = 0)
        {
            flight.Teleport(World.BasePosition + Vector3.up * 32f, heading, 35);
        }

        static void Advance(FlightController flight, float seconds, Vector2 movement, float climb = 0, float turnRate = 0)
        {
            int ticks = Mathf.CeilToInt(seconds / Dt);
            for (int i = 0; i < ticks; i++) flight.TickCommand(Dt, movement, climb, turnRate * Dt);
        }

        static string CheckAcceleration(FlightController flight)
        {
            Reset(flight);
            float time95 = -1, time99 = -1, speedAtOne = 0, maximumAcceleration = 0;
            for (int i = 0; i < 240; i++)
            {
                Vector3 before = Horizontal(flight.Velocity);
                flight.TickCommand(Dt, Vector2.up, 0, 0);
                float t = (i + 1) * Dt;
                maximumAcceleration = Mathf.Max(maximumAcceleration, (Horizontal(flight.Velocity) - before).magnitude / Dt);
                if (i == 59) speedAtOne = flight.PlanarSpeed;
                if (time95 < 0 && flight.PlanarSpeed >= flight.ForwardCruise * .95f) time95 = t;
                if (time99 < 0 && flight.PlanarSpeed >= flight.ForwardCruise * .99f) time99 = t;
            }
            Require(speedAtOne > flight.ForwardCruise * .9f, $"Unexpected one-second speed: {speedAtOne:F2}m/s.");
            Require(time95 >= .5f && time99 <= 1.2f && time99 > 0, $"Cruise onset out of range: 95%={time95:F3}s, 99%={time99:F3}s.");
            Require(Mathf.Abs(flight.PlanarSpeed - flight.ForwardCruise) < .08f, "Forward cruise speed did not settle.");
            Require(maximumAcceleration <= flight.ThrustAcceleration + .75f, $"Planar acceleration exceeded thrust bound: {maximumAcceleration:F2}.");
            return $"1s speed {speedAtOne:F2}m/s; 95% in {time95:F2}s; 99% in {time99:F2}s; cruise {flight.PlanarSpeed:F2}m/s; peak acceleration {maximumAcceleration:F2}m/s².";
        }

        static string CheckBraking(FlightController flight)
        {
            Reset(flight); Advance(flight, 4, Vector2.up);
            Vector3 start = flight.transform.position;
            float stopTime = -1, speedAtHalf = 0;
            for (int i = 0; i < 240; i++)
            {
                flight.TickCommand(Dt, Vector2.zero, 0, 0);
                if (i == 29) speedAtHalf = flight.PlanarSpeed;
                if (stopTime < 0 && flight.PlanarSpeed < .1f) stopTime = (i + 1) * Dt;
            }
            float distance = Horizontal(flight.transform.position - start).magnitude;
            Vector3 hover = flight.transform.position;
            Advance(flight, 3, Vector2.zero);
            Require(stopTime > .25f && stopTime < .7f, $"Release braking took {stopTime:F3}s.");
            Require(speedAtHalf < 2 && distance > 3 && distance < 8, $"Unexpected braking: {speedAtHalf:F2}m/s at 0.5s, {distance:F2}m stop distance.");
            Require(Horizontal(flight.transform.position - hover).magnitude < .005f, "Helicopter drifted horizontally after braking.");
            return $"Stop {stopTime:F2}s / {distance:F2}m; 0.5s speed {speedAtHalf:F2}m/s; stationary hover holds.";
        }

        static string CheckReversal(FlightController flight)
        {
            Reset(flight); Advance(flight, 4, Vector2.up);
            float crossing = -1, halfSecond = 0;
            for (int i = 0; i < 360; i++)
            {
                flight.TickCommand(Dt, Vector2.down, 0, 0);
                if (i == 29) halfSecond = flight.Velocity.z;
                if (crossing < 0 && flight.Velocity.z <= 0) crossing = (i + 1) * Dt;
            }
            Require(halfSecond < 0, $"Reverse input has not reversed velocity after 0.5s ({halfSecond:F2}m/s).");
            Require(crossing >= .2f && crossing <= .6f, $"Reversal zero crossing {crossing:F2}s outside expected range.");
            Require(Mathf.Abs(flight.Velocity.z + flight.ReverseCruise) < .1f, "Reverse cruise did not settle at its lower speed.");
            return $"Signed forward velocity {halfSecond:F2}m/s at 0.5s; crosses zero {crossing:F2}s; reverse cruise {-flight.Velocity.z:F2}m/s.";
        }

        static string CheckStrafe(FlightController flight)
        {
            Reset(flight); Advance(flight, 4, Vector2.right);
            float lateral = flight.Velocity.x;
            Require(Mathf.Abs(lateral - flight.StrafeCruise) < .1f && Mathf.Abs(flight.Velocity.z) < .02f, "Right strafe did not preserve heading-relative motion.");
            Require(Mathf.Abs(Mathf.DeltaAngle(flight.Heading, 0)) < .01f, "Strafe rotated the helicopter.");
            Reset(flight); Advance(flight, 4, Vector2.left);
            Require(Mathf.Abs(flight.Velocity.x + flight.StrafeCruise) < .1f, "Left strafe is asymmetric.");
            Reset(flight); Advance(flight, 5, Vector2.one);
            float diagonal = flight.PlanarSpeed;
            Require(diagonal <= flight.ForwardCruise + .1f && diagonal > flight.StrafeCruise, "Diagonal inputs bypass speed limits.");
            return $"Lateral cruise {lateral:F2}m/s; diagonal {diagonal:F2}m/s; heading unchanged.";
        }

        static string CheckYaw(FlightController flight)
        {
            Reset(flight);
            flight.TickCommand(Dt, Vector2.zero, 0, 90);
            float firstFrame = Mathf.Abs(Mathf.DeltaAngle(0, flight.Heading));
            Require(Mathf.Abs(firstFrame - 90) < .02f, "Mouse displacement did not steer in the same frame.");
            Advance(flight, .5f, Vector2.zero);
            Require(Mathf.Abs(Mathf.DeltaAngle(flight.Heading, 90)) < .02f && flight.YawRate == 0, "Heading continued after mouse input stopped.");
            flight.TickCommand(Dt, Vector2.zero, 0, 180);
            Require(Mathf.Abs(Mathf.DeltaAngle(flight.Heading, 270)) < .03f, "Fast 180-degree turn lost input.");
            Require(Horizontal(flight.Velocity).magnitude < .01f, "Hover yaw translated the aircraft.");
            return $"90-degree and 180-degree samples applied in their input frame; no residual turning. First frame {firstFrame:F3} degrees.";
        }

        static string CheckTurningMomentum(FlightController flight)
        {
            Reset(flight); Advance(flight, 4, Vector2.up);
            flight.TickCommand(Dt, Vector2.up, 0, 90);
            float initialMomentum = flight.Velocity.z;
            Require(initialMomentum > 22 && flight.Velocity.x < 1, "Steering rotated world velocity instantly.");
            Advance(flight, .5f - Dt, Vector2.up);
            float heading = Mathf.Abs(Mathf.DeltaAngle(0, flight.Heading));
            float velocityHeading = Mathf.Abs(Mathf.Atan2(flight.Velocity.x, flight.Velocity.z) * Mathf.Rad2Deg);
            Require(heading > 89 && velocityHeading > 20 && velocityHeading < 75, $"Unexpected turn follow-through: heading {heading:F2}°, momentum {velocityHeading:F2}°.");
            return $"After 0.5s turn: nose {heading:F1}°, momentum {velocityHeading:F1}°, original-axis speed {flight.Velocity.z:F2}m/s.";
        }

        static string CheckTerrain(FlightController flight)
        {
            float minimum = float.PositiveInfinity, minimumRendered = float.PositiveInfinity, minimumAnalytic = float.PositiveInfinity, peakRise = 0;
            int samples = 0;
            for (int route = 0; route < 12; route++)
            {
                float angle = route * 2.399963f;
                Vector3 origin = World.Ground(Mathf.Sin(angle) * World.Radius * .62f, Mathf.Cos(angle) * World.Radius * .62f);
                flight.Teleport(origin + Vector3.up * flight.MinimumClearance, route * 67f, 35);
                for (int frame = 0; frame < 420; frame++)
                {
                    flight.TickCommand(Dt, Vector2.up, -1f, (route % 3 - 1) * 8f * Dt);
                    Vector3 position = flight.transform.position;
                    float renderedClearance = position.y - World.SurfaceHeight(position.x, position.z);
                    float analyticClearance = position.y - World.Height(position.x, position.z);
                    minimum = Mathf.Min(minimum, flight.Altitude);
                    minimumRendered = Mathf.Min(minimumRendered, renderedClearance);
                    minimumAnalytic = Mathf.Min(minimumAnalytic, analyticClearance);
                    peakRise = Mathf.Max(peakRise, flight.transform.position.y - origin.y);
                    samples++;
                    Require(flight.Altitude >= flight.MinimumClearance - .015f, $"Terrain penetration on route {route}, frame {frame}: {flight.Altitude:F3}m.");
                    Require(renderedClearance >= flight.MinimumClearance - .015f && analyticClearance >= flight.MinimumClearance - .015f,
                        $"Surface penetration on route {route}, frame {frame}: rendered {renderedClearance:F3}m, analytic {analyticClearance:F3}m.");
                    Require(IsFinite(flight.Velocity) && IsFinite(flight.transform.position), "Flight became nonfinite over terrain.");
                }
            }
            return $"{samples} commanded samples across 12 terrain routes, including held descent; minimum clearance {minimum:F3}m (rendered {minimumRendered:F3}m / analytic {minimumAnalytic:F3}m); maximum terrain rise followed {peakRise:F1}m.";
        }

        static string CheckAltitude(FlightController flight)
        {
            Reset(flight);
            Vector3 origin = flight.transform.position;
            Advance(flight, 4, Vector2.zero);
            Require(Vector3.Distance(origin, flight.transform.position) < .01f, "Stationary hover bobs without altitude input.");
            flight.TickCommand(Dt, Vector2.zero, 1, 0);
            Require(flight.Velocity.y > .1f, "Climb did not start in its command frame.");
            Advance(flight, .5f, Vector2.zero, 1);
            Advance(flight, .5f, Vector2.zero, -1);
            Require(flight.Velocity.y < -5.5f, $"Rapid altitude reversal retained a climb backlog: {flight.Velocity.y:F3}m/s.");
            Advance(flight, .2f, Vector2.zero);
            Require(Mathf.Abs(flight.Velocity.y) < .1f, "Released collective did not hold altitude promptly.");
            Reset(flight);
            origin = flight.transform.position;
            flight.TickCommand(Dt, Vector2.zero, 0, 0, 0, 1);
            Advance(flight, 3, Vector2.zero);
            float scrollRise = flight.transform.position.y - origin.y;
            Require(Mathf.Abs(scrollRise - 3.5f) < .04f, $"One scroll notch moved {scrollRise:F3}m instead of 3.5m.");
            Advance(flight, 8, Vector2.zero, 1);
            Advance(flight, 2, Vector2.zero);
            float ceiling = flight.Altitude;
            Require(Mathf.Abs(ceiling - flight.MaximumAltitude) < .05f, $"Ceiling did not settle: {ceiling:F3}m.");
            Advance(flight, 10, Vector2.zero, -1);
            Require(flight.Altitude >= flight.MinimumClearance - .01f && flight.Altitude <= flight.MinimumClearance + .1f, "Descent did not safely settle at minimum clearance.");
            return $"Stationary hold stable; scroll +{scrollRise:F2}m; ceiling {ceiling:F2}m; descent floor {flight.Altitude:F2}m.";
        }

        static string CheckStableAim(FlightController flight)
        {
            Reset(flight);
            flight.CameraMotion = 1;
            Vector3 sight = flight.AimForward;
            Advance(flight, 1.2f, new Vector2(1, .3f), .5f);
            float sightDrift = Vector3.Angle(sight, flight.AimForward);
            float eyeAngle = Quaternion.Angle(flight.AimRotation, flight.Camera.transform.rotation);
            float eyeOffset = Vector3.Distance(flight.transform.position, flight.Camera.transform.position);
            Require(sightDrift < .01f, "Thrust/collective presentation altered the weapon sight.");
            Require(Mathf.Abs(flight.Bank) > 1 && eyeAngle < .05f && eyeOffset <= .036f, $"Optics or physical banking outside bounds: {eyeAngle:F3}°, {eyeOffset:F3}m.");
            flight.CameraMotion = 0;
            flight.TickCommand(Dt, Vector2.right, 0, 0);
            Require(Quaternion.Angle(flight.AimRotation, flight.Camera.transform.rotation) < .01f &&
                    Vector3.Distance(flight.AimOrigin, flight.Camera.transform.position) < .001f && Mathf.Abs(flight.Camera.fieldOfView - 78) < .01f,
                "Zero camera-motion setting did not remove translation, rotation and FOV effects.");
            return $"Stable aim drift {sightDrift:F4}°; motion-on eye deflection {eyeAngle:F2}°/{eyeOffset:F3}m; motion-off precisely matches sight.";
        }

        static string CheckFrameRates(FlightController flight)
        {
            Vector3 reference = Vector3.zero;
            float referenceHeading = 0, largestDistance = 0, largestAngle = 0;
            int[] rates = { 120, 60, 30 };
            foreach (int rate in rates)
            {
                Reset(flight);
                float dt = 1f / rate;
                for (int frame = 0; frame < rate * 7; frame++)
                {
                    float t = (float)frame / rate;
                    Vector2 movement = t < 5 ? Vector2.up : Vector2.zero;
                    float turn = t >= 3 && t < 5 ? 25f * dt : 0;
                    flight.TickCommand(dt, movement, 0, turn);
                }
                if (rate == 120) { reference = flight.transform.position; referenceHeading = flight.Heading; }
                else
                {
                    largestDistance = Mathf.Max(largestDistance, Vector3.Distance(reference, flight.transform.position));
                    largestAngle = Mathf.Max(largestAngle, Mathf.Abs(Mathf.DeltaAngle(referenceHeading, flight.Heading)));
                }
            }
            Require(largestDistance < 1f && largestAngle < 1f, $"Frame-rate drift too large: {largestDistance:F3}m / {largestAngle:F3}°.");
            return $"Seven-second accel/turn/brake route at 30/60/120Hz differs by at most {largestDistance:F3}m / {largestAngle:F3}°.";
        }

        static string CheckBoundedYaw(FlightController flight)
        {
            Reset(flight);
            flight.TickCommand(Dt, Vector2.zero, 0, 1000);
            Require(Mathf.Abs(flight.YawRate - flight.MaximumYawRate) < .02f, "Full reticle deflection did not reach maximum yaw in its input frame.");
            float start = flight.Heading;
            Advance(flight, .5f, Vector2.zero);
            Require(Mathf.Abs(Mathf.DeltaAngle(start, flight.Heading) - flight.MaximumYawRate * .5f) < .03f, "Held reticle produced unpredictable yaw.");
            float projection = 2f * Mathf.Tan(39f * Mathf.Deg2Rad) * flight.Camera.aspect;
            float recenter = -Mathf.Atan(flight.ReticleOffset.x * projection) * Mathf.Rad2Deg;
            float before = flight.Heading;
            flight.TickCommand(Dt, Vector2.zero, 0, recenter);
            Require(Mathf.Abs(flight.YawRate) < .001f, "Recentering retained yaw/backlog.");
            before = flight.Heading;
            Advance(flight, .1f, Vector2.zero);
            Require(Mathf.Abs(Mathf.DeltaAngle(before, flight.Heading)) < .001f, "Heading continued after the recentering tick.");
            flight.TickCommand(Dt, Vector2.zero, 0, -1000, 200);
            Require(flight.YawRate < -flight.MaximumYawRate + .02f, "Opposite reticle deflection did not reverse steering immediately.");
            Require(flight.LookPitch > 65 && flight.LookPitch < 79, "Lower window cannot aim steeply down.");
            Vector2 half = flight.ReticleHalfBounds;
            Vector3 viewport = flight.Camera.WorldToViewportPoint(flight.AimOrigin + flight.AimForward * 80f);
            Require(Mathf.Abs(viewport.x - .5f) <= half.x + .001f && Mathf.Abs(viewport.y - .5f) <= half.y + .001f, "Sight escaped its viewport box.");
            float pitchBefore = flight.LookPitch;
            Advance(flight, .2f, Vector2.zero);
            Require(Mathf.Abs(flight.LookPitch - pitchBefore) < .02f, "Vertical view continued after mouse stopped.");
            return $"Full rate {flight.MaximumYawRate:F0} deg/s starts, stops and reverses in one tick; 90/180 deg in {90f / flight.MaximumYawRate:F3}/{180f / flight.MaximumYawRate:F3}s. Downward elevation {pitchBefore:F1} deg; sight stays inside zone.";
        }

        static string CheckModeSwitch(FlightController flight)
        {
            flight.SetMode(FlightControlMode.BoundedReticle);
            Reset(flight);
            flight.TickCommand(Dt, Vector2.right, .5f, 8, 7);
            Vector3 aim = flight.AimForward, velocity = flight.Velocity, origin = flight.AimOrigin;
            Quaternion camera = flight.Camera.transform.rotation;
            for (int i = 0; i < 8; i++)
            {
                flight.ToggleMode();
                Require(Vector3.Dot(aim, flight.AimForward) > .999999f, "Mode switch moved weapon direction.");
                Require(Quaternion.Angle(camera, flight.Camera.transform.rotation) < .06f, "Mode switch jumped the camera.");
                Require(Vector3.Distance(origin, flight.AimOrigin) < .001f, "Mode switch jumped ray origin.");
                Require(Vector3.Distance(velocity, flight.Velocity) < .001f, "Mode switch reset world velocity.");
            }
            flight.SetMode(FlightControlMode.Direct);
            Advance(flight, .5f, Vector2.zero);
            Require(Vector3.Dot(aim, flight.AimForward) > .999999f, "Eye recentering disturbed aim.");
            return "Repeated switches preserve the exact weapon ray, camera pose and world velocity; eye trim settles without moving aim.";
        }

        static string CheckInputOwnership(Game game)
        {
            var flight = game.Flight;
            bool cursor = game.CursorFree, chart = game.Hud.ShowChart;
            bool paused = game.Paused, tuning = game.Hud.ShowFlightTuning;
            flight.SetMode(FlightControlMode.BoundedReticle);
            Reset(flight);
            flight.TickCommand(Dt, Vector2.up, 0, 20);
            float heading = flight.Heading;
            Vector2 sight = flight.ReticleOffset;
            try
            {
                game.Hud.ShowChart = true;
                game.SetCursor(true);
                for (int i = 0; i < 60; i++) flight.Tick(Dt, false);
                Require(Mathf.Abs(Mathf.DeltaAngle(heading, flight.Heading)) < .001f, "Bounded steering continued while the chart owned the mouse.");
                Require(Vector2.Distance(sight, flight.ReticleOffset) < .001f, "Chart interaction displaced the gun sight.");
                Require(flight.PlanarSpeed < .1f, "Opening UI failed to brake movement.");
                game.Paused = false;
                game.Hud.ShowFlightTuning = false;
                game.ToggleFlightTuning();
                Require(game.Paused && game.Hud.ShowFlightTuning, "F5 did not open paused flight tuning.");
                game.TogglePause();
                game.ToggleFlightTuning();
                Require(game.Paused && game.Hud.ShowFlightTuning, "F5 failed to reopen tuning after resuming.");
            }
            finally
            {
                game.Paused = paused; game.Hud.ShowFlightTuning = tuning;
                game.Hud.ShowChart = chart; game.SetCursor(cursor);
            }
            return "Chart/cursor ownership parks steering and sight and brakes the aircraft. F5 reopens tuning after resume. Recapture discards cursor-warp deltas.";
        }

        static string CheckWeaponAim(Game game)
        {
            var flight = game.Flight;
            flight.Teleport(World.Ground(-500, -700) + Vector3.up * 42, 0, 30);
            flight.TickCommand(Dt, Vector2.right, .5f, 4, 6);
            var target = new Crypt { Name = "Flight validation target", Position = flight.AimOrigin + flight.AimForward * 55 - Vector3.up * 3, Health = 10000 };
            game.Crypts.Add(target);
            try
            {
                game.Arsenal.Tick(0, false);
                Vector3 projection = flight.Camera.WorldToViewportPoint(game.Arsenal.AimPoint);
                Vector2 expected = Vector2.one * .5f + (flight.Mode == FlightControlMode.BoundedReticle ? flight.ReticleOffset : Vector2.zero);
                Require(Vector2.Distance(new Vector2(projection.x, projection.y), expected) < .001f, "HUD/weapon ray disagree during movement.");
                if (flight.Mode == FlightControlMode.BoundedReticle)
                    Require(Mathf.Abs(projection.x - .5f) > .01f && Mathf.Abs(projection.y - .5f) > .01f, "Weapon fixture failed to test an off-centre sight.");
                for (int slot = 0; slot < 4; slot++)
                {
                    var weapon = game.Arsenal.Weapons[slot];
                    bool unlocked = weapon.Unlocked;
                    float ammo = weapon.Ammo, cooldown = weapon.Cooldown, health = target.Health;
                    try
                    {
                        weapon.Unlocked = true; weapon.Ammo = 10; weapon.Cooldown = 0;
                        game.Arsenal.TryFire(slot);
                        // The launched rocket must keep its original solution as the gunship moves away.
                        if (slot == 2)
                            for (int tick = 0; tick < 50; tick++)
                            {
                                flight.TickCommand(Dt, Vector2.right, .5f, 0);
                                game.Arsenal.Tick(Dt, false);
                            }
                        Require(target.Health < health, weapon.Name + " missed the displayed ground-facing gun solution.");
                    }
                    finally { weapon.Unlocked = unlocked; weapon.Ammo = ammo; weapon.Cooldown = cooldown; }
                    // Restore target direction for the next weapon after the moving rocket pass.
                    if (slot == 2)
                    {
                        Vector3 delta = target.Position + Vector3.up * 3 - flight.AimOrigin;
                        flight.SetAim(Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg, -Mathf.Asin(delta.normalized.y) * Mathf.Rad2Deg);
                    }
                }
            }
            finally { game.Crypts.Remove(target); }
            return "Noncentral B / centered A trace matches HUD; silver, water, rocket and UV damage the actual traced fixture. Rocket keeps its launch target while moving/banking.";
        }

        static string CheckOrbit(FlightController flight)
        {
            Vector3 center = World.BasePosition;
            flight.Teleport(center + new Vector3(0, 32, -70), 0, 25);
            float maximumError = 0;
            for (int tick = 0; tick < 300; tick++)
            {
                Vector3 delta = center - flight.AimOrigin;
                float bearing = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                float error = Mathf.DeltaAngle(flight.Heading, bearing);
                float mouse;
                if (flight.Mode == FlightControlMode.Direct) mouse = Mathf.Clamp(error, -100 * Dt, 100 * Dt);
                else
                {
                    float rate = Mathf.Clamp(error * 4, -flight.MaximumYawRate, flight.MaximumYawRate);
                    float x = Mathf.Sign(rate) * (flight.SteeringDeadZone + (1 - flight.SteeringDeadZone) * Mathf.Pow(Mathf.Abs(rate) / flight.MaximumYawRate, 1f / flight.SteeringExponent));
                    float projection = 2f * Mathf.Tan(39f * Mathf.Deg2Rad) * flight.Camera.aspect;
                    mouse = (Mathf.Atan(x * flight.ReticleHalfBounds.x * projection) - Mathf.Atan(flight.ReticleOffset.x * projection)) * Mathf.Rad2Deg;
                }
                float elevation = -Mathf.Asin(delta.normalized.y) * Mathf.Rad2Deg;
                flight.TickCommand(Dt, Vector2.right, 0, mouse, Mathf.Clamp(elevation - flight.LookPitch, -1, 1));
                maximumError = Mathf.Max(maximumError, Vector3.Angle(flight.AimForward, center - flight.AimOrigin));
            }
            Require(maximumError < 22, $"Strafing target lost from the sight: {maximumError:F2} deg.");
            Require(flight.PlanarSpeed > 15 && Mathf.Abs(flight.Bank) > 1, "Orbit failed to produce lateral flight and visual banking.");
            return $"Five-second strafe/circle under real commands, peak target error {maximumError:F2} deg, speed {flight.PlanarSpeed:F2} m/s, bank {flight.Bank:F2} deg.";
        }

        static string CheckSupplyApproach(Game game)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo previous = typeof(Game).GetField("previousSupplyPosition", flags);
            FieldInfo hooked = typeof(Game).GetField("hookedSupply", flags);
            MethodInfo tick = typeof(Game).GetMethod("TickSupplies", flags);
            object oldPrevious = previous.GetValue(game), oldHooked = hooked.GetValue(game);
            float fuel = game.Fuel;
            int collected = game.SuppliesCollected;
            Supply supply = game.SpawnSupply(World.Ground(-500, -700), SupplyKind.Fuel);
            Require(supply != null, "Could not create isolated approach supply.");
            try
            {
                game.Fuel = 40;
                game.Flight.Teleport(supply.Position + new Vector3(0, 8, -35), 0, 35);
                previous.SetValue(game, game.Flight.transform.position);
                hooked.SetValue(game, null);
                object[] arguments = { Dt };
                for (int i = 0; i < 180; i++)
                {
                    game.Flight.TickCommand(Dt, i < 100 ? Vector2.up : Vector2.zero, 0, 0);
                    tick.Invoke(game, arguments);
                }
                float distance = Game.HorizontalDistance(supply.Position, game.Flight.transform.position);
                Require(supply.Collected && game.Fuel > 40, "Low commanded approach failed to hook and deliver fuel.");
                Require(distance < 8 && game.Flight.PlanarSpeed < .1f, $"Approach overshot its hover: {distance:F2}m.");
                return $"Low approach hooks real fuel cargo and stops {distance:F2}m from crate; stable hover after braking.";
            }
            finally
            {
                game.Fuel = fuel; game.SuppliesCollected = collected;
                previous.SetValue(game, oldPrevious); hooked.SetValue(game, oldHooked);
                game.Supplies.Remove(supply);
                if (supply.Visual) UnityEngine.Object.Destroy(supply.Visual.gameObject);
            }
        }

        static string CheckAllocations(FlightController flight)
        {
            Reset(flight);
            for (int i = 0; i < 120; i++) flight.TickCommand(Dt, new Vector2(.4f, .8f), .2f, .01f);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 240; i++) flight.TickCommand(Dt, new Vector2(.4f, .8f), .2f, .01f);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Require(bytes == 0, $"Flight tick allocated {bytes} managed bytes over 240 warm ticks.");
            return $"{bytes} managed bytes across 240 warm flight ticks (movement, steering, climb, presentation and lamp); excludes existing HUD/game systems.";
        }

        static Vector3 Horizontal(Vector3 value) => new Vector3(value.x, 0, value.z);
        static bool IsFinite(Vector3 value) => !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z));
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
