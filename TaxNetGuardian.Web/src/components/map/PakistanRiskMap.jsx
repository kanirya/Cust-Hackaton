import React, { useEffect, useRef } from "react";
import L from "leaflet";
import "leaflet/dist/leaflet.css";

// Real coordinates + province for Pakistani cities/areas used across the sandbox data.
const CITY = {
  Karachi: [24.8607, 67.0011, "Sindh"],
  Lahore: [31.5204, 74.3587, "Punjab"],
  Islamabad: [33.6844, 73.0479, "Islamabad"],
  Rawalpindi: [33.5651, 73.0169, "Punjab"],
  Peshawar: [34.0151, 71.5249, "Khyber Pakhtunkhwa"],
  Quetta: [30.1798, 66.9750, "Balochistan"],
  Multan: [30.1575, 71.5249, "Punjab"],
  Faisalabad: [31.4504, 73.1350, "Punjab"],
  Hyderabad: [25.396, 68.3578, "Sindh"],
  Gujranwala: [32.1877, 74.1945, "Punjab"],
  Sialkot: [32.4945, 74.5229, "Punjab"],
  Bahawalpur: [29.3956, 71.6836, "Punjab"],
  Sargodha: [32.0836, 72.6711, "Punjab"],
  Sukkur: [27.7052, 68.8574, "Sindh"],
  Larkana: [27.5598, 68.2264, "Sindh"],
  Mardan: [34.1989, 72.0231, "Khyber Pakhtunkhwa"],
  Abbottabad: [34.1688, 73.2215, "Khyber Pakhtunkhwa"],
  Gwadar: [25.1264, 62.3225, "Balochistan"],
  Parachinar: [33.8990, 70.0917, "Khyber Pakhtunkhwa"],
  Mingora: [34.7795, 72.3614, "Khyber Pakhtunkhwa"],
  Bannu: [32.9889, 70.6056, "Khyber Pakhtunkhwa"],
  "Dera Ghazi Khan": [30.0561, 70.6403, "Punjab"],
  Sahiwal: [30.6660, 73.1114, "Punjab"],
  Nawabshah: [26.2442, 68.4099, "Sindh"],
  Kohat: [33.5869, 71.4414, "Khyber Pakhtunkhwa"],
  Mirpur: [33.1469, 73.7517, "Azad Kashmir"],
  Muzaffarabad: [34.3700, 73.4711, "Azad Kashmir"],
  Gilgit: [35.9208, 74.3144, "Gilgit-Baltistan"],
  Turbat: [26.0031, 63.0544, "Balochistan"],
  Chitral: [35.8511, 71.7864, "Khyber Pakhtunkhwa"]
};

const PAKISTAN_BOUNDS = [[23.5, 60.8], [37.1, 77.9]];

function severity(count) {
  if (count >= 6) return { name: "Critical", color: "#dc2626", ring: "rgba(220,38,38,0.35)" };
  if (count >= 3) return { name: "High", color: "#f59e0b", ring: "rgba(245,158,11,0.35)" };
  return { name: "Moderate", color: "#10b981", ring: "rgba(16,185,129,0.30)" };
}

function PakistanRiskMap({ casesByCity, onSelectCity }) {
  const elRef = useRef(null);
  const mapRef = useRef(null);
  const layerRef = useRef(null);
  const onSelectRef = useRef(onSelectCity);
  onSelectRef.current = onSelectCity;

  useEffect(() => {
    if (mapRef.current || !elRef.current) return;
    const map = L.map(elRef.current, {
      zoomControl: true,
      attributionControl: true,
      scrollWheelZoom: false,
      minZoom: 4,
      maxZoom: 12
    });
    map.fitBounds(PAKISTAN_BOUNDS);

    const light = L.tileLayer("https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png", {
      attribution: "&copy; OpenStreetMap &copy; CARTO", subdomains: "abcd", maxZoom: 19
    });
    const dark = L.tileLayer("https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png", {
      attribution: "&copy; OpenStreetMap &copy; CARTO", subdomains: "abcd", maxZoom: 19
    });
    const streets = L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
      attribution: "&copy; OpenStreetMap", maxZoom: 19
    });
    const satellite = L.tileLayer("https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}", {
      attribution: "&copy; Esri", maxZoom: 19
    });
    light.addTo(map);
    L.control.layers(
      { "Light": light, "Dark": dark, "Streets": streets, "Satellite": satellite },
      null,
      { position: "topright", collapsed: true }
    ).addTo(map);

    // Custom "recenter on Pakistan" control.
    const Recenter = L.Control.extend({
      options: { position: "topleft" },
      onAdd() {
        const btn = L.DomUtil.create("button", "map-recenter");
        btn.innerHTML = "⤢";
        btn.title = "Reset to Pakistan";
        L.DomEvent.on(btn, "click", (e) => { L.DomEvent.stop(e); map.fitBounds(PAKISTAN_BOUNDS); });
        return btn;
      }
    });
    map.addControl(new Recenter());

    layerRef.current = L.layerGroup().addTo(map);
    mapRef.current = map;

    // Wire popup "Open case queue" buttons.
    map.on("popupopen", (e) => {
      const node = e.popup.getElement()?.querySelector(".map-pop-btn");
      if (node) {
        L.DomEvent.on(node, "click", (ev) => {
          L.DomEvent.stop(ev);
          const city = node.getAttribute("data-city");
          if (city && onSelectRef.current) onSelectRef.current(city);
          map.closePopup();
        });
      }
    });

    setTimeout(() => map.invalidateSize(), 200);
    return () => { map.remove(); mapRef.current = null; };
  }, []);

  useEffect(() => {
    const map = mapRef.current;
    const layer = layerRef.current;
    if (!map || !layer) return;
    layer.clearLayers();

    const entries = Object.entries(casesByCity || {});
    const pts = [];
    entries.forEach(([city, count]) => {
      const coords = CITY[city];
      if (!coords || !count) return;
      const [lat, lng, province] = coords;
      const s = severity(count);
      const size = Math.min(46, 26 + Number(count) * 2);
      const critical = s.name === "Critical";

      const html = `
        <div class="map-pin ${critical ? "pulse" : ""}" style="--pin:${s.color};--ring:${s.ring};width:${size}px;height:${size}px">
          <span>${count}</span>
        </div>`;
      const icon = L.divIcon({
        html,
        className: "map-pin-wrap",
        iconSize: [size, size],
        iconAnchor: [size / 2, size / 2]
      });
      const marker = L.marker([lat, lng], { icon }).addTo(layer);
      marker.bindPopup(`
        <div class="map-pop">
          <div class="map-pop-head"><strong>${city}</strong><span class="map-pop-sev" style="color:${s.color}">${s.name}</span></div>
          <div class="map-pop-sub">${province}</div>
          <div class="map-pop-count"><b>${count}</b> active case${count > 1 ? "s" : ""}</div>
          <button class="map-pop-btn" data-city="${city}">Open case queue →</button>
        </div>`, { closeButton: true, className: "map-pop-wrap" });
      marker.bindTooltip(`${city}: ${count}`, { direction: "top", offset: [0, -size / 2], className: "map-tip" });
      pts.push([lat, lng]);
    });

    if (pts.length > 1) {
      map.fitBounds(L.latLngBounds(pts).pad(0.35), { maxZoom: 7 });
    } else if (pts.length === 1) {
      map.setView(pts[0], 7);
    } else {
      map.fitBounds(PAKISTAN_BOUNDS);
    }
  }, [casesByCity]);

  const total = Object.values(casesByCity || {}).reduce((a, b) => a + Number(b || 0), 0);
  const cityCount = Object.keys(casesByCity || {}).length;

  return (
    <div className="pak-geo-wrap">
      <div ref={elRef} className="pak-geo-map" />
      <div className="pak-geo-legend">
        <span><i style={{ background: "#dc2626" }} /> Critical (6+)</span>
        <span><i style={{ background: "#f59e0b" }} /> High (3–5)</span>
        <span><i style={{ background: "#10b981" }} /> Moderate (1–2)</span>
        <span className="pak-geo-total">{total} cases · {cityCount} hubs</span>
      </div>
    </div>
  );
}

export { PakistanRiskMap };
