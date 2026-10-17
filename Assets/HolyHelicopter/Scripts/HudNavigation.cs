using UnityEngine;

namespace HolyHelicopter
{
    public sealed partial class Hud
    {
        public bool ShowChart;
        public bool HasWaypoint;
        public Vector3 Waypoint;
        public string WaypointName = "WAYPOINT";
        readonly Horde.ThreatRadarPoint[] threats = new Horde.ThreatRadarPoint[48];
        int threatCount;
        Texture2D chartTerrain;
        Vector3 chartClick;

        Vector3 NavigationTarget(out string name)
        {
            if (HasWaypoint) { name = WaypointName; return Waypoint; }
            if (game.Passengers >= 4 || game.CanExtract || game.Fuel < 28 || game.Hull < 30)
            { name = "SANCTUARY"; return World.BasePosition; }
            float best = float.MaxValue;
            Vector3 result = World.BasePosition; name = "SANCTUARY";
            foreach (var c in game.Crypts)
            {
                if (!c.Active && c.RelicRecovered) continue;
                float d = Game.HorizontalDistance(game.Flight.transform.position, c.Position);
                if (d >= best) continue;
                best = d; result = c.Position; name = c.Active ? c.Name.ToUpperInvariant() : "RECOVER GOLDEN SEAL";
            }
            if (best == float.MaxValue && game.Rescued + game.Passengers < game.ExtractionQuota)
                foreach (var s in game.Survivors)
                {
                    float d = Game.HorizontalDistance(game.Flight.transform.position, s.Position);
                    if (!s.Alive || d >= best) continue;
                    best = d; result = s.Position; name = "SURVIVORS / " + World.VillageName(s.Village).ToUpperInvariant();
                }
            return result;
        }

        void DrawNavigation(float heading)
        {
            Vector3 target = NavigationTarget(out string name);
            Vector3 delta = target - game.Flight.transform.position;
            float angle = Mathf.DeltaAngle(heading, Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg);
            string arrow = Mathf.Abs(angle) < 10 ? "v" : angle < 0 ? "<" : ">";
            Text(new Rect(455, 112, 690, 24), $"{arrow} {name}  {Game.HorizontalDistance(target, game.Flight.transform.position):0} m  / G CHART", 17, Cyan, TextAnchor.MiddleCenter);
            float x = 800 + Mathf.Clamp(angle, -63, 63) * 4.6f;
            MarkerGlyph(new Vector2(x, 78), Cyan, MarkerKind.Objective, .8f);
        }

        void DrawAirThreats()
        {
            // Stable, capped priority markers instead of thousands of enemy labels.
            if (Event.current.type == EventType.Repaint)
                threatCount = game.Horde.GetThreatRadarPoints(game.Flight.transform.position, 230, threats);
            int shown = 0;
            for (int i = 0; i < threatCount && shown < 8; i++)
            {
                var t = threats[i];
                if (t.Kind != EnemyKind.Gargoyle && t.Kind != EnemyKind.Necromancer) continue;
                Vector3 p = game.Flight.Camera.WorldToViewportPoint(t.Position + Vector3.up * 2);
                Vector2 point;
                if (p.z > 0 && p.x > .12f && p.x < .88f && p.y > .25f && p.y < .73f)
                    point = new Vector2(p.x * Width, (1 - p.y) * Height);
                else
                {
                    Vector3 local = game.Flight.transform.InverseTransformDirection(t.Position - game.Flight.transform.position);
                    float bearing = Mathf.Atan2(local.x, local.z);
                    point = new Vector2(800 + Mathf.Sin(bearing) * 520, 450 - Mathf.Cos(bearing) * 185);
                }
                Color color = t.Heavy ? Hazard : new Color(.85f,.5f,1);
                MarkerGlyph(point, color, MarkerKind.Objective, .8f);
                Text(new Rect(point.x - 65, point.y - 27, 130, 23), t.Heavy ? "GARGOYLE" : "HEX CASTER", 15, color, TextAnchor.MiddleCenter);
                shown++;
            }
        }

        Vector2 ChartPoint(Vector3 p, Rect map)
            => new Vector2(map.x + (p.x / (World.Radius * 2) + .5f) * map.width,
                map.y + (.5f - p.z / (World.Radius * 2)) * map.height);

        void DrawChart()
        {
            if (!chartTerrain)
            {
                chartTerrain = new Texture2D(128,128,TextureFormat.RGB24,false) { name="Parish contour chart", filterMode=FilterMode.Bilinear };
                Color[] colors = new Color[128*128];
                for (int y=0;y<128;y++) for(int x=0;x<128;x++)
                {
                    float h=World.Height((x/127f-.5f)*World.Radius*2,(y/127f-.5f)*World.Radius*2);
                    Color c=Color.Lerp(new Color(.065f,.14f,.15f),new Color(.28f,.34f,.27f),Mathf.Clamp01(h/80));
                    if (Mathf.Repeat(h,8)<.8f) c*=.7f;
                    colors[y*128+x]=c;
                }
                chartTerrain.SetPixels(colors); chartTerrain.Apply();
            }
            Box(new Rect(260,145,1080,610),Ink,Gold);
            Text(new Rect(283,158,1034,32),"DEADWICK PARISH / FLIGHT CHART",26,Gold);
            Rect map=new Rect(285,204,520,520);
            Image(map,chartTerrain,Color.white);
            for(int i=1;i<4;i++)
            {
                Line(new Vector2(map.x+i*130,map.y),new Vector2(map.x+i*130,map.yMax),1,Edge);
                Line(new Vector2(map.x,map.y+i*130),new Vector2(map.xMax,map.y+i*130),1,Edge);
            }
            foreach(var s in game.Supplies) if(!s.Collected)
                MarkerGlyph(ChartPoint(s.Position,map),s.Kind==SupplyKind.Relic?Gold:Cyan,MarkerKind.Crate,.45f);
            foreach(var c in game.Crypts)
            {
                Vector2 p=ChartPoint(c.Position,map);
                MarkerGlyph(p,c.Active?new Color(.9f,.45f,1):Gold,MarkerKind.Objective,1.2f);
                Text(new Rect(p.x-85,p.y+9,170,22),c.Name.ToUpperInvariant(),14,c.Active?Ivory:Gold,TextAnchor.MiddleCenter);
            }
            foreach(var s in game.Settlements.Sites)
            {
                Vector2 p=ChartPoint(s.Position,map);
                Circle(p,6,s.Overrun?Red:s.UnderAttack?Hazard:Friendly);
                Text(new Rect(p.x-130,p.y+8,260,25),s.Name.ToUpperInvariant(),14,Ivory,TextAnchor.MiddleCenter);
            }
            Vector2 home=ChartPoint(World.BasePosition,map);
            MarkerGlyph(home,Gold,MarkerKind.Objective,1.2f);
            Text(new Rect(home.x-85,home.y+10,170,25),"SANCTUARY",16,Gold,TextAnchor.MiddleCenter);
            Vector2 pilot=ChartPoint(game.Flight.transform.position,map);
            Circle(pilot,5,Color.white);
            float a=game.Flight.transform.eulerAngles.y*Mathf.Deg2Rad;
            Line(pilot,pilot+new Vector2(Mathf.Sin(a),-Mathf.Cos(a))*18,3,Gold);
            Vector3 nav=NavigationTarget(out string navName);
            Vector2 destination=ChartPoint(nav,map);
            Line(pilot,destination,1,Cyan); Ring(destination,11,Cyan,16);
            Text(new Rect(829,207,480,55),"CLICK MAP TO SET COURSE\nRIGHT CLICK / AUTOMATIC GUIDANCE",19,Cyan,TextAnchor.UpperLeft,true);
            for(int i=0;i<game.Settlements.Sites.Length;i++)
            {
                var s=game.Settlements.Sites[i]; float y=285+i*100;
                Text(new Rect(829,y,475,28),s.Name.ToUpperInvariant(),22,Gold);
                Text(new Rect(829,y+31,475,57),$"{s.Status} / {s.Integrity:0}%\n{s.AliveResidents} survivors / {(s.Secured?"FIELD REPAIR AVAILABLE":"FIELD AID CLOSED")}",18,s.Overrun?Red:Ivory,TextAnchor.UpperLeft,true);
            }
            Text(new Rect(829,594,475,94),"Golden seals use cargo space. People use four rescue seats. Hover low at village crosses for field repairs. Sanctuary fully rearms and refuels.",20,Muted,TextAnchor.UpperLeft,true);
            Text(new Rect(829,696,475,28),"G / CLOSE CHART — WORLD REMAINS LIVE",17,Gold);
            if(Event.current.type==EventType.MouseDown && map.Contains(Event.current.mousePosition))
            {
                if(Event.current.button==1) HasWaypoint=false;
                else
                {
                    Vector2 p=Event.current.mousePosition;
                    Waypoint=World.Ground(((p.x-map.x)/map.width-.5f)*World.Radius*2,(.5f-(p.y-map.y)/map.height)*World.Radius*2);
                    chartClick=Waypoint;
                    HasWaypoint=true; WaypointName="WAYPOINT";
                    float best=100;
                    foreach(var c in game.Crypts) SnapWaypoint(c.Position,c.Name,ref best);
                    foreach(var s in game.Settlements.Sites) SnapWaypoint(s.Position,s.Name,ref best);
                    SnapWaypoint(World.BasePosition,"SANCTUARY",ref best);
                    foreach(var s in game.Supplies) if(!s.Collected) SnapWaypoint(s.Position,SupplyLabels[(int)s.Kind],ref best);
                }
                Event.current.Use();
            }
        }

        void SnapWaypoint(Vector3 point,string name,ref float best)
        {
            float d=Game.HorizontalDistance(point,chartClick);
            if(d>=best)return;
            best=d; Waypoint=point; WaypointName=name.ToUpperInvariant();
        }
    }
}
