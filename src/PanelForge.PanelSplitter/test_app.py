"""Self-check: trang tổng hợp 3 panel (1 trên, 2 dưới) + 1 bong bóng elip ở panel 2.
Chạy: py test_app.py
"""
import cv2
import numpy as np
from app import split_page

img = np.full((1200, 800, 3), 250, np.uint8)
for x, y, w, h in [(20, 20, 760, 500), (20, 540, 370, 640), (410, 540, 370, 640)]:
    cv2.rectangle(img, (x, y), (x + w, y + h), (0, 0, 0), 6)
cv2.ellipse(img, (200, 700), (120, 60), 0, 0, 360, (0, 0, 0), 4)

r = split_page(img)
assert [p["bbox"][:2] for p in r["panels"]] == [[17, 17], [17, 537], [407, 537]], r["panels"]
assert len(r["bubbles"]) == 1 and r["bubbles"][0]["panelOrder"] == 2, r["bubbles"]
assert [p["bbox"][0] for p in split_page(img, rtl=True)["panels"]][1:] == [407, 17]
print("ok")
