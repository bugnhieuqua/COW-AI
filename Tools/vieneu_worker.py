import json
import os
import sys
import traceback

os.environ.setdefault("HF_HUB_DISABLE_TELEMETRY", "1")
os.environ.setdefault("TOKENIZERS_PARALLELISM", "false")

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")
if hasattr(sys.stdin, "reconfigure"):
    sys.stdin.reconfigure(encoding="utf-8")

PREFIX = "@@VIENEU_JSON@@"


def emit(payload: dict) -> None:
    sys.stdout.write(PREFIX + json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def main() -> int:
    src_dir = sys.argv[1] if len(sys.argv) > 1 else r"G:\Video\auto-compare-video\VieNeu-TTS\src"
    if src_dir not in sys.path:
        sys.path.insert(0, src_dir)

    emit({
        "event": "loading",
        "message": "⏳ Đang khởi động tiến trình và nạp model âm thanh VieNeu-TTS v3 Turbo 48kHz lên Server..."
    })

    try:
        from vieneu import Vieneu
        tts = Vieneu(mode="v3turbo")
    except Exception as exc:
        emit({
            "event": "error",
            "error": f"Không thể nạp model VieNeu-TTS v3 Turbo: {exc}",
            "detail": traceback.format_exc()
        })
        return 1

    emit({
        "event": "ready",
        "backend": getattr(tts, "backend", "v3turbo"),
        "sampleRate": getattr(tts, "sample_rate", 48000),
        "message": "✅ Server âm thanh VieNeu-TTS 48kHz đã sẵn sàng."
    })

    for raw_line in sys.stdin:
        line = raw_line.strip()
        if not line:
            continue
        req_id = ""
        try:
            req = json.loads(line)
            if req.get("cmd") == "ping":
                emit({"event": "pong", "id": req.get("id", "")})
                continue
            if req.get("cmd") == "shutdown":
                break

            req_id = req.get("id", "")
            text = (req.get("text") or "").strip()
            voice_name = (req.get("voice") or "Hải Đăng").strip()
            output_path = (req.get("outputPath") or "").strip()

            if not text or not output_path:
                emit({"event": "result", "id": req_id, "success": False, "error": "Thiếu văn bản hoặc đường dẫn xuất âm thanh."})
                continue

            out_dir = os.path.dirname(output_path)
            if out_dir:
                os.makedirs(out_dir, exist_ok=True)

            try:
                voice_data = tts.get_preset_voice(voice_name)
            except Exception:
                voice_data = tts.get_preset_voice("Hải Đăng")

            audio = tts.infer(text=text, voice=voice_data)
            tts.save(audio, output_path)
            emit({
                "event": "result",
                "id": req_id,
                "success": True,
                "outputPath": output_path,
                "voice": voice_name
            })
        except Exception as exc:
            emit({
                "event": "result",
                "id": req_id,
                "success": False,
                "error": str(exc)
            })

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
