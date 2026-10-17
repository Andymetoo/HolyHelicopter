using System;
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
            game.Fuel = 100;
            try
            {
                Check("Flight: thrust reaches cruise progressively", report, () => CheckAcceleration(flight));
                Check("Flight: released controls brake over distance", report, () => CheckBraking(flight));
                Check("Flight: reverse thrust preserves inertia", report, () => CheckReversal(flight));
                Check("Flight: lateral and diagonal cruise limits", report, () => CheckStrafe(flight));
                Check("Flight: bounded yaw preserves mouse intent", report, () => CheckYaw(flight));
                Check("Flight: turning preserves world momentum", report, () => CheckTurningMomentum(flight));
                Check("Flight: terrain clearance under real commands", report, () => CheckTerrain(flight));
                Check("Flight: altitude hold, climb and scroll", report, () => CheckAltitude(flight));
                Check("Flight: presentation does not move weapon aim", report, () => CheckStableAim(flight));
                Check("Flight: command path is stable across frame rates", report, () => CheckFrameRates(flight));
            }
            finally
            {
                flight.CameraMotion = originalMotion;
                game.Fuel = originalFuel;
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
            Require(speedAtOne > 4 && speedAtOne < 11, $"Unexpected one-second speed: {speedAtOne:F2}m/s.");
            Require(time95 >= 2.35f && time99 <= 3.10f && time99 > 0, $"Cruise onset out of range: 95%={time95:F3}s, 99%={time99:F3}s.");
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
            Require(stopTime > 1.6f && stopTime < 2.15f, $"Release braking took {stopTime:F3}s.");
            Require(speedAtHalf > 14 && distance > 17 && distance < 29, $"Braking lacks expected inertia: {speedAtHalf:F2}m/s at 0.5s, {distance:F2}m stop distance.");
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
            Require(halfSecond > 15, $"Reverse input cancelled existing velocity too quickly ({halfSecond:F2}m/s after 0.5s).");
            Require(crossing >= 2.2f && crossing <= 3.2f, $"Reversal zero crossing {crossing:F2}s outside expected range.");
            Require(Mathf.Abs(flight.Velocity.z + flight.ReverseCruise) < .1f, "Reverse cruise did not settle at its lower speed.");
            return $"Still forward {halfSecond:F2}m/s at 0.5s; crosses zero {crossing:F2}s; reverse cruise {-flight.Velocity.z:F2}m/s.";
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
            float maxRate = 0, maxAngularAcceleration = 0;
            flight.TickCommand(Dt, Vector2.zero, 0, 90);
            float firstFrame = Mathf.Abs(Mathf.DeltaAngle(0, flight.Heading));
            float priorRate = flight.YawRate;
            for (int i = 0; i < 210; i++)
            {
                flight.TickCommand(Dt, Vector2.zero, 0, 0);
                maxRate = Mathf.Max(maxRate, Mathf.Abs(flight.YawRate));
                maxAngularAcceleration = Mathf.Max(maxAngularAcceleration, Mathf.Abs(flight.YawRate - priorRate) / Dt);
                priorRate = flight.YawRate;
            }
            Require(firstFrame < 1f, $"Heading snapped {firstFrame:F2}° on a single mouse-intent sample.");
            Require(Mathf.Abs(Mathf.DeltaAngle(flight.Heading, 90)) < .05f, "Queued mouse intent was lost before reaching 90°.");
            Require(maxRate <= flight.MaximumYawRate + .02f && maxAngularAcceleration <= flight.YawAcceleration + .1f,
                $"Angular bounds exceeded: {maxRate:F2}°/s, {maxAngularAcceleration:F2}°/s².");
            Require(Horizontal(flight.Velocity).magnitude < .01f, "Hover yaw translated the aircraft.");
            return $"Single 90° intent reaches {flight.Heading:F3}°; first frame {firstFrame:F3}°; peak {maxRate:F1}°/s, {maxAngularAcceleration:F1}°/s².";
        }

        static string CheckTurningMomentum(FlightController flight)
        {
            Reset(flight); Advance(flight, 4, Vector2.up);
            flight.TickCommand(Dt, Vector2.up, 0, 90);
            Advance(flight, .5f - Dt, Vector2.up);
            float heading = Mathf.Abs(Mathf.DeltaAngle(0, flight.Heading));
            float velocityHeading = Mathf.Abs(Mathf.Atan2(flight.Velocity.x, flight.Velocity.z) * Mathf.Rad2Deg);
            Require(heading > 20 && velocityHeading < heading * .55f, $"Velocity followed the nose instantly: heading {heading:F2}°, momentum {velocityHeading:F2}°.");
            Require(flight.Velocity.z > 19, "Turn discarded existing forward world velocity.");
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
            float eyeOffset = Vector3.Distance(flight.AimOrigin, flight.Camera.transform.position);
            Require(sightDrift < .01f, "Thrust/collective presentation altered the weapon sight.");
            Require(eyeAngle > .15f && eyeAngle < 5.2f && eyeOffset <= .10f, $"Camera motion outside restrained bounds: {eyeAngle:F3}°, {eyeOffset:F3}m.");
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

        static Vector3 Horizontal(Vector3 value) => new Vector3(value.x, 0, value.z);
        static bool IsFinite(Vector3 value) => !(float.IsNaN(value.x) || float.IsNaN(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.x) || float.IsInfinity(value.y) || float.IsInfinity(value.z));
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
