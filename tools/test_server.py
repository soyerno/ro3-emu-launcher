#!/usr/bin/env python3
"""
KonsorRO DevKit - Verificador de Conectividad y Endpoints
Comprueba el estado de la web, panel admin, registro y túneles locales.
"""

import urllib.request
import urllib.parse
import json
import socket
import sys

HEADERS = {"User-Agent": "KonsorRO-Launcher-DevKit/1.0"}

def check_http(name, url, expected_codes=(200,)):
    try:
        req = urllib.request.Request(url, headers=HEADERS)
        with urllib.request.urlopen(req, timeout=5) as resp:
            data = resp.read().decode('utf-8')
            print(f" [OK] {name}: HTTP {resp.status}")
            try:
                js = json.loads(data)
                return js
            except Exception:
                return data
    except urllib.error.HTTPError as e:
        if e.code in expected_codes:
            print(f" [OK] {name}: HTTP {e.code} (esperado)")
        else:
            print(f" [WARN] {name}: HTTP {e.code}")
        try:
            return json.loads(e.read().decode('utf-8'))
        except Exception:
            return None
    except Exception as e:
        print(f" [FALLO] {name}: {e}")
        return None

def check_port(host, port):
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.settimeout(2.0)
    try:
        s.connect((host, port))
        s.close()
        return True
    except Exception:
        return False

def main():
    print("========================================================")
    print("     KonsorRO DevKit - Diagnostico de Servidor")
    print("========================================================\n")

    print("[1] Endpoints Publicos Web:")
    status = check_http("Status Servidor (konsor.com.ar/api/status)", "https://konsor.com.ar/api/status")
    if status and isinstance(status, dict):
        print(f"     -> Online: {status.get('online')}, Jugadores: {status.get('players')}")

    reg_status = check_http("Estado Registro (konsor.com.ar/api/admin/registration)", "https://konsor.com.ar/api/admin/registration")
    if reg_status and isinstance(reg_status, dict):
        print(f"     -> Registro habilitado: {reg_status.get('enabled')}")

    check_http("Status JSON estatico", "https://konsor.com.ar/status.json")
    check_http("Noticias JSON", "https://konsor.com.ar/news.json")

    print("\n[2] Puertos de Tuneles Locales Cloudflare:")
    ports = [(5380, "HTTP / Login"), (20000, "Microservicios"), (33100, "Juego TCP")]
    for p, desc in ports:
        active = check_port("127.0.0.1", p)
        state = "[OK] Activo" if active else "[INFO] Inactivo (se activa al abrir el launcher)"
        print(f"     Puerto {p:5d} ({desc}): {state}")

    print("\n========================================================")
    print(" Diagnostico completado.")
    print("========================================================")

if __name__ == "__main__":
    main()
