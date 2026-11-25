"""PanelForge Panel Splitter: tách panel + bong bóng thoại từ ảnh 1 trang manga (OpenCV, kiểu Kumiko).

Chạy: py -m uvicorn app:app --port 5066   (trong thư mục này)
Chỉ trả gợi ý toạ độ, không lưu gì - người dùng xác nhận ở phía PanelForge.
"""
import cv2
import numpy as np
from fastapi import FastAPI, File, HTTPException, Query, UploadFile

app = FastAPI(title="PanelForge Panel Splitter")


def split_page(img, rtl=False):
    H, W = img.shape[:2]
    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)

    # Panels: viền tối -> contour ngoài cùng -> bbox đủ lớn
    _, dark = cv2.threshold(gray, 100, 255, cv2.THRESH_BINARY_INV)
    contours, _ = cv2.findContours(dark, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    # ponytail: ngưỡng diện tích 4% cố định; panel rất nhỏ / không viền cần model (Magi)
    panels = [list(cv2.boundingRect(c)) for c in contours
              if cv2.boundingRect(c)[2] * cv2.boundingRect(c)[3] >= 0.04 * W * H]

    # Thứ tự đọc: theo hàng (y), trong hàng theo x (đảo nếu RTL)
    panels.sort(key=lambda p: p[1])
    rows, row = [], []
    for p in panels:
        if row and p[1] > row[0][1] + row[0][3] * 0.5:
            rows.append(row)
            row = []
        row.append(p)
    if row:
        rows.append(row)
    panels = [p for r in rows for p in sorted(r, key=lambda p: -p[0] if rtl else p[0])]

    # Bong bóng: vùng sáng kín, gần elip, nằm trong panel
    _, light = cv2.threshold(gray, 200, 255, cv2.THRESH_BINARY)
    contours, _ = cv2.findContours(light, cv2.RETR_CCOMP, cv2.CHAIN_APPROX_SIMPLE)
    bubbles = []
    for c in contours:
        area = cv2.contourArea(c)
        if area < 0.003 * W * H or len(c) < 5:
            continue
        x, y, w, h = cv2.boundingRect(c)
        _, (ma, mi), _ = cv2.fitEllipse(c)
        if not 0.85 < area / (np.pi * ma * mi / 4) < 1.15:
            continue
        cx, cy = x + w / 2, y + h / 2
        idx = next((i for i, (px, py, pw, ph) in enumerate(panels)
                    if px <= cx <= px + pw and py <= cy <= py + ph), None)
        if idx is None or w * h > 0.5 * panels[idx][2] * panels[idx][3]:
            continue
        poly = cv2.approxPolyDP(c, 2, True).reshape(-1, 2).tolist()
        bubbles.append({"panelOrder": idx + 1, "bbox": [x, y, w, h], "polygon": poly})

    # Viền bong bóng cho 2 contour (trong/ngoài) -> giữ cái nằm trong
    def inside(a, b):
        return a[0] >= b[0] and a[1] >= b[1] and a[0] + a[2] <= b[0] + b[2] and a[1] + a[3] <= b[1] + b[3]
    bubbles = [b for b in bubbles if not any(o is not b and inside(o["bbox"], b["bbox"]) for o in bubbles)]
    bubbles.sort(key=lambda b: (b["panelOrder"], b["bbox"][1]))

    return {"width": W, "height": H,
            "panels": [{"order": i + 1, "bbox": p} for i, p in enumerate(panels)],
            "bubbles": bubbles}


@app.get("/health")
def health():
    return {"status": "ok"}


@app.post("/split")
async def split(file: UploadFile = File(...), rtl: bool = Query(False)):
    data = np.frombuffer(await file.read(), np.uint8)
    img = cv2.imdecode(data, cv2.IMREAD_COLOR) if data.size else None
    if img is None:
        raise HTTPException(400, "File không phải ảnh hợp lệ.")
    return split_page(img, rtl)
