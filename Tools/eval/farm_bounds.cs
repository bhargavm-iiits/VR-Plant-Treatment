string B(string name) { var go = UnityEngine.GameObject.Find(name); if (!go) return name + " none"; var rs = go.GetComponentsInChildren<UnityEngine.Renderer>(); if (rs.Length == 0) return name + " no renderers"; var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return name + " min=" + b.min.ToString("F1") + " max=" + b.max.ToString("F1"); }
var sb = new System.Text.StringBuilder();
foreach (var n in new[]{"GreenhousePark","TimberFence","Crops","Orchard","DiseaseGallery","NoticeBoard","Navigation","Environment"}) sb.AppendLine(B(n));
var crops = UnityEngine.GameObject.Find("Crops");
foreach (UnityEngine.Transform c in crops.transform) { var rs = c.GetComponentsInChildren<UnityEngine.Renderer>(); if (rs.Length==0) continue; var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); sb.AppendLine("  crops/" + c.name + " min=" + b.min.ToString("F1") + " max=" + b.max.ToString("F1")); }
var gh = UnityEngine.GameObject.Find("GreenhousePark");
foreach (UnityEngine.Transform c in gh.transform) { var rs = c.GetComponentsInChildren<UnityEngine.Renderer>(); if (rs.Length==0) continue; var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); sb.AppendLine("  gh/" + c.name + " min=" + b.min.ToString("F1") + " max=" + b.max.ToString("F1")); }
var nav = UnityEngine.GameObject.Find("Navigation");
foreach (UnityEngine.Transform c in nav.transform) sb.AppendLine("  nav/" + c.name + " " + c.position.ToString("F1"));
return sb.ToString();
