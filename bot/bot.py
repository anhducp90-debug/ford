"""Telegram bot cấp key VPN (VLESS Reality) + QR cho Happ.

- Người lạ /start -> admin nhận nút Duyệt / Từ chối.
- Người được duyệt dùng /key để nhận link + QR (mỗi người 1 key: tg<id>).
- Admin: /users, /revoke <id>, /allow <id>.
"""
import asyncio
import html
import io
import json
import logging
import os
import re
import subprocess
from pathlib import Path

import qrcode
from telegram import InlineKeyboardButton, InlineKeyboardMarkup, Update
from telegram.constants import ParseMode
from telegram.ext import (Application, CallbackQueryHandler, CommandHandler,
                          ContextTypes)

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("vpnbot")

TOKEN = os.environ["BOT_TOKEN"]
ADMINS = {int(x) for x in os.environ.get("ADMIN_IDS", "").replace(" ", "").split(",") if x}
DATA = Path(os.environ.get("DATA_FILE", "/var/lib/vpnbot/users.json"))
VPNCTL = ["sudo", "-n", "/usr/local/bin/vpnctl"]
LOCK = asyncio.Lock()

if not ADMINS:
    raise SystemExit("ADMIN_IDS trống")


# ---------- lưu trữ ----------
def load() -> set[int]:
    try:
        return set(json.loads(DATA.read_text())["allowed"])
    except (FileNotFoundError, KeyError, ValueError):
        return set()


def save(allowed: set[int]) -> None:
    DATA.parent.mkdir(parents=True, exist_ok=True)
    DATA.write_text(json.dumps({"allowed": sorted(allowed)}))


def is_allowed(uid: int) -> bool:
    return uid in ADMINS or uid in load()


# ---------- vpnctl ----------
def _run(*args: str) -> subprocess.CompletedProcess:
    return subprocess.run([*VPNCTL, *args], capture_output=True, text=True, timeout=30)


def _get_or_create_link(uid: int) -> str:
    name = f"tg{uid}"
    r = _run("link", name)
    if r.returncode != 0:
        r = _run("add", name)
    if r.returncode != 0:
        raise RuntimeError(r.stderr.strip() or "vpnctl lỗi")
    for line in r.stdout.splitlines():
        if line.startswith("vless://"):
            return line.strip()
    raise RuntimeError("Không đọc được link từ vpnctl")


def _qr_png(data: str) -> io.BytesIO:
    buf = io.BytesIO()
    qrcode.make(data, border=2, box_size=8).save(buf, format="PNG")
    buf.seek(0)
    return buf


async def send_key(bot, chat_id: int, uid: int) -> None:
    async with LOCK:
        link = await asyncio.to_thread(_get_or_create_link, uid)
    caption = (
        "🔑 <b>Ключ готов / Ваш ключ</b>\n\n"
        f"<code>{html.escape(link)}</code>\n\n"
        "Happ → <b>+</b> → <i>Import from clipboard</i> (скопируйте ссылку) "
        "или отсканируйте QR."
    )
    await bot.send_photo(chat_id, _qr_png(link), caption=caption, parse_mode=ParseMode.HTML)


# ---------- handlers ----------
async def start(update: Update, ctx: ContextTypes.DEFAULT_TYPE) -> None:
    u = update.effective_user
    if is_allowed(u.id):
        await update.message.reply_text("Привет! /key — получить ключ и QR для Happ.")
        return
    await update.message.reply_text(
        "Доступ закрыт. Запрос отправлен администратору. / Đã gửi yêu cầu tới admin."
    )
    kb = InlineKeyboardMarkup([[
        InlineKeyboardButton("✅ Duyệt", callback_data=f"ok:{u.id}"),
        InlineKeyboardButton("❌ Từ chối", callback_data=f"no:{u.id}"),
    ]])
    who = f"{u.full_name} (@{u.username or '-'}) id={u.id}"
    for a in ADMINS:
        try:
            await ctx.bot.send_message(a, f"Yêu cầu truy cập: {who}", reply_markup=kb)
        except Exception:  # admin chưa /start bot
            log.warning("không gửi được cho admin %s", a)


async def key(update: Update, ctx: ContextTypes.DEFAULT_TYPE) -> None:
    uid = update.effective_user.id
    if not is_allowed(uid):
        await update.message.reply_text("Нет доступа. Gửi /start để xin quyền.")
        return
    try:
        await send_key(ctx.bot, update.effective_chat.id, uid)
    except Exception as e:
        log.exception("tạo key lỗi")
        await update.message.reply_text(f"Lỗi tạo key: {e}")


async def on_button(update: Update, ctx: ContextTypes.DEFAULT_TYPE) -> None:
    q = update.callback_query
    if q.from_user.id not in ADMINS:
        await q.answer("Chỉ admin", show_alert=True)
        return
    action, _, raw = q.data.partition(":")
    uid = int(raw)
    await q.answer()
    if action == "ok":
        allowed = load(); allowed.add(uid); save(allowed)
        await q.edit_message_text(f"✅ Đã duyệt {uid}")
        try:
            await ctx.bot.send_message(uid, "✅ Доступ разрешён. Отправьте /key")
        except Exception:
            pass
    else:
        await q.edit_message_text(f"❌ Đã từ chối {uid}")


def admin_only(fn):
    async def w(update: Update, ctx: ContextTypes.DEFAULT_TYPE):
        if update.effective_user.id not in ADMINS:
            return
        await fn(update, ctx)
    return w


def _uid_arg(ctx) -> int | None:
    return int(ctx.args[0]) if ctx.args and re.fullmatch(r"\d{1,15}", ctx.args[0]) else None


@admin_only
async def allow(update, ctx):
    uid = _uid_arg(ctx)
    if uid is None:
        await update.message.reply_text("Dùng: /allow <telegram_id>")
        return
    allowed = load(); allowed.add(uid); save(allowed)
    await update.message.reply_text(f"✅ Đã cho phép {uid}")


@admin_only
async def revoke(update, ctx):
    uid = _uid_arg(ctx)
    if uid is None:
        await update.message.reply_text("Dùng: /revoke <telegram_id>")
        return
    allowed = load(); allowed.discard(uid); save(allowed)
    async with LOCK:
        await asyncio.to_thread(_run, "del", f"tg{uid}")
    await update.message.reply_text(f"🗑 Đã thu hồi key và quyền của {uid}")


@admin_only
async def users(update, ctx):
    allowed = sorted(load())
    await update.message.reply_text(
        "Người dùng được duyệt:\n" + ("\n".join(map(str, allowed)) or "(trống)")
    )


def main() -> None:
    app = Application.builder().token(TOKEN).build()
    app.add_handler(CommandHandler("start", start))
    app.add_handler(CommandHandler("key", key))
    app.add_handler(CommandHandler("allow", allow))
    app.add_handler(CommandHandler("revoke", revoke))
    app.add_handler(CommandHandler("users", users))
    app.add_handler(CallbackQueryHandler(on_button, pattern=r"^(ok|no):\d+$"))
    app.run_polling()


if __name__ == "__main__":
    main()
