#!/usr/bin/env python3
"""
RO3 Launcher DevKit - Logo and icon processor
Toma cualquier imagen (PNG, JPG, WEBP), remueve el fondo claro mediante flood-fill
de precisión, recorta al contenido útil y genera tanto el PNG transparente del
launcher como el icono de Windows app.ico con resoluciones múltiples.
"""

import sys
import os
from collections import deque

try:
    from PIL import Image, ImageFilter
    import numpy as np
except ImportError:
    print("[ERROR] Se requiere Pillow y numpy. Instálalos con: pip install pillow numpy")
    sys.exit(1)

def process_logo(input_path, output_dir):
    if not os.path.exists(input_path):
        print(f"[ERROR] No se encuentra el archivo: {input_path}")
        return False

    print(f"[1/4] Abriendo {input_path}...")
    im = Image.open(input_path).convert('RGB')
    arr = np.array(im, dtype=np.int32)
    h, w, _ = arr.shape

    # Distancia al color blanco/crema de fondo
    dist = np.max(np.abs(arr - 255), axis=2)

    visited = np.zeros((h, w), dtype=bool)
    queue = deque()

    # Sembrar bordes de la imagen
    tolerance = 22
    for x in range(w):
        if dist[0, x] < tolerance: queue.append((0, x)); visited[0, x] = True
        if dist[h-1, x] < tolerance: queue.append((h-1, x)); visited[h-1, x] = True
    for y in range(h):
        if dist[y, 0] < tolerance and not visited[y, 0]: queue.append((y, 0)); visited[y, 0] = True
        if dist[y, w-1] < tolerance and not visited[y, w-1]: queue.append((y, w-1)); visited[y, w-1] = True

    print("[2/4] Removiendo fondo mediante flood-fill de precisión...")
    while queue:
        cy, cx = queue.popleft()
        for ny, nx in ((cy+1, cx), (cy-1, cx), (cy, cx+1), (cy, cx-1)):
            if 0 <= ny < h and 0 <= nx < w and not visited[ny, nx]:
                if dist[ny, nx] < tolerance:
                    visited[ny, nx] = True
                    queue.append((ny, nx))

    # Construir imagen RGBA
    rgba = np.zeros((h, w, 4), dtype=np.uint8)
    rgba[:, :, :3] = np.array(im, dtype=np.uint8)
    rgba[:, :, 3] = np.where(visited, 0, 255)

    # Suavizado antialiasing
    alpha_img = Image.fromarray(rgba[:, :, 3], mode='L')
    alpha_smooth = alpha_img.filter(ImageFilter.SMOOTH)
    rgba[:, :, 3] = np.array(alpha_smooth)

    out_img = Image.fromarray(rgba, mode='RGBA')
    bbox = out_img.getbbox()
    if bbox:
        cropped = out_img.crop(bbox)
    else:
        cropped = out_img

    # 1. Guardar ui.assets.logo-hero.png
    png_path = os.path.join(output_dir, "ui.assets.logo-hero.png")
    cropped.save(png_path, format="PNG")
    print(f"[3/4] PNG del launcher guardado: {png_path} ({cropped.width}x{cropped.height})")

    # 2. Guardar app.ico multirresolución
    ico_path = os.path.join(output_dir, "app.ico")
    ico_sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)]
    max_dim = max(cropped.size)
    square = Image.new('RGBA', (max_dim, max_dim), (0, 0, 0, 0))
    offset = ((max_dim - cropped.width) // 2, (max_dim - cropped.height) // 2)
    square.paste(cropped, offset)
    square.save(ico_path, format='ICO', sizes=ico_sizes)
    print(f"[4/4] Icono de aplicación generado: {ico_path} (hasta 256x256)")

    print("\n[EXITO] Logo e icono actualizados correctamente en el DevKit.")
    return True

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python process_logo.py <image_path>")
        sys.exit(1)
    else:
        input_file = sys.argv[1]

    dev_root = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
    process_logo(input_file, dev_root)
