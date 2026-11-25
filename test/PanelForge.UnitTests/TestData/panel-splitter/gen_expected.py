"""Sinh ảnh trang mẫu + expected.json bằng split_page của app.py (nguồn chuẩn parity)."""
import importlib.util, json, sys, types
from pathlib import Path
import cv2, numpy as np

app_py, out = Path(sys.argv[1]), Path(sys.argv[2])
out.mkdir(parents=True, exist_ok=True)

# app.py import fastapi ở đầu file; chỉ cần split_page nên stub nếu máy không có fastapi
if importlib.util.find_spec("fastapi") is None:
    fa = types.ModuleType("fastapi")
    class _App:
        def __init__(self, **k): pass
        def get(self, *a, **k): return lambda f: f
        def post(self, *a, **k): return lambda f: f
    fa.FastAPI = _App; fa.File = fa.Query = lambda *a, **k: None
    fa.HTTPException = Exception; fa.UploadFile = object
    sys.modules["fastapi"] = fa
spec = importlib.util.spec_from_file_location("app", app_py)
app = importlib.util.module_from_spec(spec); spec.loader.exec_module(app)

rng = np.random.default_rng(490)

def page_grid():  # 3 hàng, có bong bóng đơn và bong bóng viền đôi
    img = np.full((1800, 1200, 3), 255, np.uint8)
    for (x, y, w, h) in [(40, 40, 540, 520), (620, 40, 540, 520), (40, 600, 1120, 560),
                         (40, 1200, 360, 560), (440, 1200, 720, 560)]:
        cv2.rectangle(img, (x, y), (x + w, y + h), (0, 0, 0), 6)
    cv2.ellipse(img, (310, 200), (150, 80), 0, 0, 360, (0, 0, 0), 4)
    cv2.ellipse(img, (900, 300), (120, 120), 0, 0, 360, (0, 0, 0), 3)
    cv2.ellipse(img, (900, 300), (110, 110), 0, 0, 360, (0, 0, 0), 3)
    cv2.ellipse(img, (700, 850), (160, 70), 15, 0, 360, (0, 0, 0), 4)
    return img

def page_noisy():  # khung lệch, nét vẽ, nhiễu nhẹ
    img = np.full((1600, 1100, 3), 255, np.uint8)
    cv2.rectangle(img, (30, 30), (1070, 500), (0, 0, 0), 5)
    cv2.polylines(img, [np.array([[30, 540], [600, 560], [580, 1050], [30, 1030]])], True, (0, 0, 0), 5)
    cv2.rectangle(img, (640, 560), (1070, 1050), (0, 0, 0), 5)
    cv2.rectangle(img, (30, 1090), (1070, 1570), (0, 0, 0), 5)
    for _ in range(60):
        p = rng.integers([60, 60], [1040, 1540]); q = p + rng.integers(-60, 60, 2)
        cv2.line(img, tuple(int(v) for v in p), tuple(int(v) for v in q), (40, 40, 40), 2)
    cv2.ellipse(img, (300, 260), (140, 90), 0, 0, 360, (0, 0, 0), 4)
    cv2.ellipse(img, (850, 800), (120, 80), -10, 0, 360, (0, 0, 0), 4)
    noise = rng.normal(0, 6, img.shape)
    return np.clip(img + noise, 0, 255).astype(np.uint8)

def page_large():  # > 4000px
    img = np.full((4500, 3000, 3), 255, np.uint8)
    for (x, y, w, h) in [(100, 100, 1350, 1400), (1550, 100, 1350, 1400), (100, 1600, 2800, 1400), (100, 3100, 2800, 1300)]:
        cv2.rectangle(img, (x, y), (x + w, y + h), (0, 0, 0), 10)
    cv2.ellipse(img, (2200, 600), (350, 200), 0, 0, 360, (0, 0, 0), 8)
    cv2.ellipse(img, (900, 2200), (300, 300), 0, 0, 360, (0, 0, 0), 8)
    return img

expected = {}
for name, make in [("grid", page_grid), ("noisy", page_noisy), ("large", page_large)]:
    img = make()
    cv2.imwrite(str(out / f"{name}.png"), img)
    reread = cv2.imread(str(out / f"{name}.png"), cv2.IMREAD_COLOR)  # đúng bytes mà .NET sẽ đọc
    expected[name] = {"ltr": app.split_page(reread, False), "rtl": app.split_page(reread, True)}
    r = expected[name]["ltr"]; print(name, r["width"], r["height"], len(r["panels"]), "panels", len(r["bubbles"]), "bubbles")

(out / "expected.json").write_text(json.dumps(expected, indent=1), encoding="utf-8")
