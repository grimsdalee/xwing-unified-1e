#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def main() -> None:
    print("URL validation scaffold ready.")
    print(f"Repository root: {ROOT}")

if __name__ == "__main__":
    main()
