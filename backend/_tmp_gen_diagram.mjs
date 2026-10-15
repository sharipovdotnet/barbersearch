import fs from "node:fs";

// ---------- helpers ----------
const nonce = () => Math.floor(Math.random() * 2 ** 31);
const uid = () => Math.random().toString(36).slice(2, 12) + Math.random().toString(36).slice(2, 6);
const now = Date.now();

const COLORS = {
  ink: "#1e1e1e",
  ar: "#e03131",
  muted: "#868e96",
  bandFill: "#f8f9fa",
  bandStroke: "#ced4da",
  nested: "#f1f3f5",
  newMark: "#2f9e44",
};

const elements = [];

function rect(x, y, w, h, opts = {}) {
  const el = {
    id: opts.id ?? uid(),
    type: "rectangle",
    x, y, width: w, height: h,
    angle: 0,
    strokeColor: opts.strokeColor ?? COLORS.ink,
    backgroundColor: opts.backgroundColor ?? "transparent",
    fillStyle: "solid",
    strokeWidth: opts.strokeWidth ?? 2,
    strokeStyle: opts.strokeStyle ?? "solid",
    roughness: opts.roughness ?? 1,
    opacity: 100,
    groupIds: [],
    frameId: null,
    roundness: opts.roundness === null ? null : { type: 3 },
    seed: nonce(),
    version: 1,
    versionNonce: nonce(),
    isDeleted: false,
    boundElements: [],
    updated: now,
    link: null,
    locked: false,
  };
  elements.push(el);
  return el;
}

function text(x, y, w, str, opts = {}) {
  const fontSize = opts.fontSize ?? 14;
  const lines = str.split("\n").length;
  const el = {
    id: uid(),
    type: "text",
    x, y, width: w, height: lines * fontSize * 1.25,
    angle: 0,
    strokeColor: opts.color ?? COLORS.ink,
    backgroundColor: "transparent",
    fillStyle: "solid",
    strokeWidth: 1,
    strokeStyle: "solid",
    roughness: 1,
    opacity: 100,
    groupIds: [],
    frameId: null,
    roundness: null,
    seed: nonce(),
    version: 1,
    versionNonce: nonce(),
    isDeleted: false,
    boundElements: [],
    updated: now,
    link: null,
    locked: false,
    text: str,
    originalText: str,
    fontSize,
    fontFamily: opts.fontFamily ?? 2,
    textAlign: opts.align ?? "center",
    verticalAlign: "top",
    containerId: null,
    originalContainerId: null,
    lineHeight: 1.25,
    autoResize: true,
    baseline: fontSize,
  };
  elements.push(el);
  return el;
}

function arrow(from, to, opts = {}) {
  const fc = { x: from.x + from.width / 2, y: from.y + from.height / 2 };
  const tc = { x: to.x + to.width / 2, y: to.y + to.height / 2 };
  let dx = tc.x - fc.x;
  let dy = tc.y - fc.y;
  const len = Math.hypot(dx, dy) || 1;
  dx /= len; dy /= len;

  const edge = (c, r, ux, uy) => {
    const sx = ux !== 0 ? r.width / 2 / Math.abs(ux) : Infinity;
    const sy = uy !== 0 ? r.height / 2 / Math.abs(uy) : Infinity;
    const t = Math.min(sx, sy);
    return { x: c.x + ux * t, y: c.y + uy * t };
  };

  const gap = opts.gap ?? 6;
  const s = edge(fc, from, dx, dy);
  const e = edge(tc, to, -dx, -dy);
  const sp = { x: s.x + dx * gap, y: s.y + dy * gap };
  const ep = { x: e.x - dx * gap, y: e.y - dy * gap };

  const x0 = Math.min(sp.x, ep.x);
  const y0 = Math.min(sp.y, ep.y);

  const el = {
    id: uid(),
    type: "arrow",
    x: x0, y: y0,
    width: Math.abs(ep.x - sp.x),
    height: Math.abs(ep.y - sp.y),
    angle: 0,
    strokeColor: opts.color ?? COLORS.ink,
    backgroundColor: "transparent",
    fillStyle: "solid",
    strokeWidth: opts.strokeWidth ?? 2,
    strokeStyle: opts.strokeStyle ?? "solid",
    roughness: 1,
    opacity: 100,
    groupIds: [],
    frameId: null,
    roundness: null,
    seed: nonce(),
    version: 1,
    versionNonce: nonce(),
    isDeleted: false,
    boundElements: [],
    updated: now,
    link: null,
    locked: false,
    points: [[sp.x - x0, sp.y - y0], [ep.x - x0, ep.y - y0]],
    lastCommittedPoint: null,
    startBinding: { elementId: from.id, focus: 0, gap: 4 },
    endBinding: { elementId: to.id, focus: 0, gap: 4 },
    startArrowhead: null,
    endArrowhead: "arrow",
    elbowed: false,
  };
  elements.push(el);
  from.boundElements.push({ type: "arrow", id: el.id });
  to.boundElements.push({ type: "arrow", id: el.id });
  return el;
}

// ---------- layout ----------
const TOP = 300;
const PAD = 20;
const GAP = 24;
const HEADER = 60;

const nodeHeight = (fields) => 34 + fields.length * 17 + 16;

const bands = [
  {
    title: "Identity",
    x: 0, w: 440,
    nodes: [
      { key: "User", ar: true, fields: ["Email (уникальный)", "PasswordHash", "FirstName, LastName", "PhoneNumber?, AvatarUrl?", "Role* — Customer | Barber | Admin", "IsEmailVerified, IsPhoneVerified", "IsActive, LastLoginAt?"] },
      { key: "FavoriteBarber", fields: ["UserId", "BarberProfileId", "уникально (UserId, BarberProfileId)"] },
    ],
  },
  {
    title: "Catalog — справочники (ведёт админ)",
    x: 480, w: 440,
    nodes: [
      { key: "Salon", ar: true, fields: ["Name, Address", "City, Country", "Location — double*", "TimeZoneId*", "IsActive"] },
      { key: "HairService", ar: true, fields: ["Name, Category", "DefaultDurationMinutes (> 0)", "IsActive"] },
      { key: "Specialization", ar: true, fields: ["Name, IsActive"] },
    ],
  },
  {
    title: "Barbers",
    x: 960, w: 620,
    nodes: [
      { key: "BarberProfile", ar: true, fields: ["UserId", "DisplayName, Grade (меняет админ)", "Languages[], YearsOfExperience", "CertificatesAndTraining[], Achievements[]", "PaymentMethods[], CommunicationFormat[]", "Goodies[], Bio?, LatePolicy", "IsActive, IsVerified", "Rating (0..100), ReviewsCount"] },
      { key: "BarberSalon", nested: true, height: 216, fields: ["SalonId, IsPrimary", "ровно один primary"] },
      { key: "BarberService", fields: ["HairServiceId", "Price, DurationMinutes", "IsActive"] },
      { key: "BarberSpecialization", fields: ["SpecializationId"] },
      { key: "PortfolioPhoto", fields: ["Url, SortOrder", "IsPublished (≤ 10 шт.)"] },
      { key: "TimeOff", fields: ["StartAt, EndAt", "Reason? — не пересекаются"] },
    ],
  },
  {
    title: "Booking",
    x: 1620, w: 420,
    nodes: [
      { key: "Booking", ar: true, fields: ["UserId, BarberProfileId", "BarberServiceId", "SalonId*", "StartAt, EndAt", "Price (снимок)", "Status", "IdempotencyKey*", "CancelledAt?, CancelReason?"] },
    ],
  },
  {
    title: "Reviews",
    x: 2080, w: 400,
    nodes: [
      { key: "Review", ar: true, fields: ["BookingId — обязателен*", "UserId, BarberProfileId", "Rating (1..5)", "Text?, IsPublished"] },
    ],
  },
];

const nodes = {};

for (const band of bands) {
  let y = TOP + HEADER;
  const heights = [];
  for (const n of band.nodes) {
    const h = n.height ?? nodeHeight(n.fields);
    n.y = y;
    n.x = band.x + PAD;
    n.w = band.w - PAD * 2;
    n.h = h;
    heights.push(h);
    y += h + GAP;
  }
  band.height = HEADER + heights.reduce((a, b) => a + b, 0) + GAP * (band.nodes.length - 1) + PAD;

  const bandRect = rect(band.x, TOP, band.w, band.height, {
    strokeColor: COLORS.bandStroke, backgroundColor: COLORS.bandFill, strokeWidth: 2, roughness: 0,
  });
  text(band.x + PAD, TOP + 16, band.w - PAD * 2, band.title, { fontSize: 20, color: COLORS.muted, align: "left" });

  for (const n of band.nodes) {
    const color = n.ar ? COLORS.ar : COLORS.ink;
    const r = rect(n.x, n.y, n.w, n.h, {
      strokeColor: color,
      strokeWidth: n.ar ? 3 : 2,
      backgroundColor: "#ffffff",
    });
    nodes[n.key] = r;
    n.rect = r;
  }
}

// markers for nested / label rendering
const bandOf = (key) => bands.find((b) => b.nodes.some((n) => n.key === key));

// nested WorkingSchedule inside BarberSalon
const bs = bandOf("BarberSalon").nodes.find((n) => n.key === "BarberSalon");
const inner = rect(bs.x + PAD, bs.y + 96, bs.w - PAD * 2, bs.h - 116, {
  strokeColor: COLORS.muted, backgroundColor: COLORS.nested, strokeWidth: 1, roughness: 0,
});

// ---------- arrows: ownership (solid) ----------
const own = [
  ["User", "FavoriteBarber"],
  ["BarberProfile", "BarberSalon"],
  ["BarberProfile", "BarberService"],
  ["BarberProfile", "BarberSpecialization"],
  ["BarberProfile", "PortfolioPhoto"],
  ["BarberProfile", "TimeOff"],
];
for (const [a, b] of own) arrow(nodes[a], nodes[b], { color: COLORS.ink, strokeWidth: 2 });

// ---------- arrows: references by Id (dashed) ----------
const refs = [
  ["FavoriteBarber", "BarberProfile"],
  ["BarberProfile", "User"],
  ["BarberSalon", "Salon"],
  ["BarberService", "HairService"],
  ["BarberSpecialization", "Specialization"],
  ["Booking", "BarberProfile"],
  ["Booking", "BarberService"],
  ["Booking", "User"],
  ["Booking", "Salon"],
  ["Review", "Booking"],
  ["Review", "BarberProfile"],
  ["Review", "User"],
];
for (const [a, b] of refs) arrow(nodes[a], nodes[b], { color: COLORS.muted, strokeWidth: 1, strokeStyle: "dashed" });

// ---------- texts on top of arrows ----------
for (const band of bands) {
  for (const n of band.nodes) {
    text(n.x + 12, n.y + 12, n.w - 24, n.key, { fontSize: 19, color: n.ar ? COLORS.ar : COLORS.ink });
    text(n.x + 16, n.y + 44, n.w - 32, n.fields.join("\n"), { fontSize: 13, color: COLORS.muted });
    if (n.nested) {
      text(inner.x + 10, inner.y + 10, inner.width - 20, "WorkingSchedule", { fontSize: 15, color: COLORS.ink });
      text(inner.x + 10, inner.y + 36, inner.width - 20, "DayOfWeek, StartTime, EndTime, IsDayOff\nодна запись на (BarberSalon, DayOfWeek)\nребёнок BarberSalon, а не BarberProfile*", { fontSize: 12, color: COLORS.muted });
    }
  }
}

// ---------- title, legend, note ----------
text(0, 0, 1200, "BarberSearch — контексты и границы агрегатов (draft)", { fontSize: 30, align: "left", color: COLORS.ink });

const legendLines = [
  "Красная рамка — агрегатный корень (7 шт.)   ·   чёрная рамка — дочерняя сущность (6 шт.)",
  "Сплошная стрелка — владение: ребёнок живёт внутри агрегата и меняется только через его методы",
  "Пунктир — ссылка по Id между агрегатами: навигаций нет, наружу торчит только Id",
  "Серый блок — контекст (папка в BarberSearch.Domain, не отдельная сборка)",
  "* — добавлено или изменено относительно текущего кода",
];
const legend = rect(0, 50, 1240, 34 + legendLines.length * 22 + 16, {
  strokeColor: COLORS.bandStroke, backgroundColor: "#ffffff", strokeWidth: 1, roughness: 0,
});
text(legend.x + 16, legend.y + 14, legend.width - 32, legendLines.join("\n"), { fontSize: 14, color: COLORS.ink, align: "left" });

const maxBottom = Math.max(...bands.map((b) => TOP + b.height));
const noteLines = [
  "Слоты — это чтение в Application, а не метод агрегата:",
  "  BarberSalon.WorkingSchedule(день) − TimeOff − Booking(Status ∈ {Pending, Confirmed})",
  "Запрос пересекает контексты Barbers и Booking, поэтому живёт в query-слое.",
  "",
  "BarberProfile.Rating пересчитывается при публикации/скрытии отзыва (Review + Rating — две агрегата,",
  "допускаем одну транзакцию). Доменные события — только для того, что не держит транзакцию:",
  "уведомления и переиндексация поиска.",
];
const note = rect(960, maxBottom + 60, 1520, 34 + noteLines.length * 20 + 16, {
  strokeColor: COLORS.bandStroke, backgroundColor: "#fff9db", strokeWidth: 1, roughness: 0,
});
text(note.x + 16, note.y + 14, note.width - 32, noteLines.join("\n"), { fontSize: 13, color: COLORS.ink, align: "left" });

// ---------- write ----------
const scene = {
  type: "excalidraw",
  version: 2,
  source: "dsh-generated",
  elements,
  appState: { gridSize: null, viewBackgroundColor: "#ffffff" },
  files: {},
};
fs.writeFileSync("domain-model.excalidraw", JSON.stringify(scene, null, 2));

// ---------- self-check ----------
const rects = elements.filter((e) => e.type === "rectangle");
const arrows = elements.filter((e) => e.type === "arrow");
const ids = new Set(elements.map((e) => e.id));
let bad = 0;
for (const a of arrows) {
  if (!ids.has(a.startBinding.elementId) || !ids.has(a.endBinding.elementId)) bad++;
}
const overlaps = [];
const nodeRects = bands.flatMap((b) => b.nodes.map((n) => ({ k: n.key, r: n.rect })));
for (let i = 0; i < nodeRects.length; i++) {
  for (let j = i + 1; j < nodeRects.length; j++) {
    const a = nodeRects[i].r, b = nodeRects[j].r;
    if (a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height) {
      overlaps.push(`${nodeRects[i].k} ∩ ${nodeRects[j].k}`);
    }
  }
}
console.log("elements:", elements.length, "| rects:", rects.length, "| arrows:", arrows.length);
console.log("dangling bindings:", bad);
console.log("node overlaps:", overlaps.length ? overlaps.join(", ") : "none");
console.log("canvas:", Math.round(Math.max(...bands.map((b) => b.x + b.w))), "x", Math.round(note.y + note.height));
