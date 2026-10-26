#!/usr/bin/env python3
"""Client for the Warudo automation Playground plugin.

Connects to the loopback automation port of a running Warudo, sends each
positional argument as one command, and prints each framed reply. The protocol
is the MikanXR editor's automation protocol, so the same framing and the same
client-side verbs apply.

    python Tools/Automation/warudo_automate.py "app info"
    python Tools/Automation/warudo_automate.py "warudo assets" "obj inspect $MikanXR:MikanClient"
    python Tools/Automation/warudo_automate.py launch "warudo asset create MikanXR" "log tail 40"

Client-side arguments (not sent to the server):
    ready                        wait until the server answers "app info"
    sleep:<s>                    pause that many seconds instead of the default delay
    until:<command>:<expected>   poll the command until a reply line starts with
                                 the expected text (fails after --timeout seconds)
    launch                       start Warudo.exe from --warudo unless it is running,
                                 then wait until the plugin answers
    quit                         ask Warudo to quit and wait for the process to exit
    kill                         terminate Warudo.exe without letting it save
    restart                      quit, then launch
    run:<command>                run a shell command (an install script), failing
                                 the drive if it exits nonzero

Exits nonzero if the connection cannot be opened, a reply times out, a run
command fails, or an until condition never becomes true.
"""

import argparse
import os
import socket
import subprocess
import sys
import time

DEFAULT_PORT = 21140
DEFAULT_WARUDO_DIR = os.environ.get("WARUDO_DIR", r"D:\Program Files (x86)\Steam\steamapps\common\Warudo")
PROCESS_NAME = "Warudo.exe"
CONNECT_RETRY_SECONDS = 0.25


class AutomationClient:
    def __init__(self, port, connect_timeout, reply_timeout):
        self.reply_timeout = reply_timeout
        deadline = time.monotonic() + connect_timeout
        while True:
            try:
                self.sock = socket.create_connection(("127.0.0.1", port), timeout=reply_timeout)
                break
            except OSError:
                if time.monotonic() >= deadline:
                    raise SystemExit(
                        f"error: could not connect to 127.0.0.1:{port}\n"
                        "is Warudo running with the automation scripts in its Playground folder? "
                        "(Tools/Automation/install.bat)"
                    )
                time.sleep(CONNECT_RETRY_SECONDS)
        self.buffer = b""

    def close(self):
        try:
            self.sock.close()
        except OSError:
            pass

    def read_line(self):
        deadline = time.monotonic() + self.reply_timeout
        while b"\n" not in self.buffer:
            if time.monotonic() >= deadline:
                raise SystemExit("error: timed out waiting for a reply line")
            try:
                chunk = self.sock.recv(65536)
            except TimeoutError:
                raise SystemExit("error: timed out waiting for a reply line")
            except OSError:
                raise SystemExit("error: server closed the connection")
            if not chunk:
                raise SystemExit("error: server closed the connection")
            self.buffer += chunk
        line, self.buffer = self.buffer.split(b"\n", 1)
        return line.decode("utf-8", errors="replace").rstrip("\r")

    def send_command(self, command):
        """Send one command and return the reply's content lines."""
        self.sock.sendall(command.encode("utf-8") + b"\n")
        count_line = self.read_line()
        try:
            count = int(count_line)
        except ValueError:
            raise SystemExit(f"error: expected a reply count line, got '{count_line}'")
        return [self.read_line() for _ in range(count)]


def warudo_running():
    result = subprocess.run(
        ["tasklist", "/FI", f"IMAGENAME eq {PROCESS_NAME}", "/NH"],
        capture_output=True, text=True,
    )
    return PROCESS_NAME.lower() in result.stdout.lower()


def wait_for_exit(timeout):
    deadline = time.monotonic() + timeout
    while warudo_running():
        if time.monotonic() >= deadline:
            return False
        time.sleep(0.25)
    return True


class Session:
    """Holds the connection across commands, reconnecting after a launch."""

    def __init__(self, args):
        self.args = args
        self.client = None

    def connected(self):
        if self.client is None:
            self.client = AutomationClient(self.args.port, self.args.wait, self.args.timeout)
        return self.client

    def disconnect(self):
        if self.client is not None:
            self.client.close()
            self.client = None

    def launch(self):
        if warudo_running():
            print(f"> launch: {PROCESS_NAME} is already running")
        else:
            exe = os.path.join(self.args.warudo, PROCESS_NAME)
            if not os.path.isfile(exe):
                raise SystemExit(f"error: no {PROCESS_NAME} in {self.args.warudo}")
            command = [exe]
            if self.args.port != DEFAULT_PORT:
                command.append(f"-automationPort={self.args.port}")
            print(f"> launch: {' '.join(command)}")
            flags = subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP
            subprocess.Popen(command, cwd=self.args.warudo, creationflags=flags, close_fds=True)

        # Warudo takes a while to load its scene and compile Playground, so the connect waits the launch budget
        self.disconnect()
        self.client = AutomationClient(self.args.port, self.args.launch_wait, self.args.timeout)
        for line in self.client.send_command("app info"):
            print(line)

    def quit(self):
        if not warudo_running():
            print(f"> quit: {PROCESS_NAME} is not running")
            self.disconnect()
            return
        print("> quit")
        try:
            self.connected().send_command("app quit")
        except SystemExit as e:
            print(f"warning: {e}", file=sys.stderr)
        self.disconnect()
        if not wait_for_exit(self.args.quit_wait):
            raise SystemExit(f"error: {PROCESS_NAME} did not exit within {self.args.quit_wait}s (use kill)")

    def kill(self):
        print("> kill")
        self.disconnect()
        subprocess.run(["taskkill", "/F", "/IM", PROCESS_NAME], capture_output=True)
        if not wait_for_exit(self.args.quit_wait):
            raise SystemExit(f"error: {PROCESS_NAME} is still running")


def main():
    parser = argparse.ArgumentParser(description="Drive the Warudo automation Playground plugin.")
    parser.add_argument("commands", nargs="+", help="commands to send, in order")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--warudo", default=DEFAULT_WARUDO_DIR, help="Warudo install folder (WARUDO_DIR)")
    parser.add_argument("--wait", type=float, default=20.0, help="seconds to retry the initial connect")
    parser.add_argument("--launch-wait", type=float, default=120.0, help="seconds to wait for a launched Warudo to answer")
    parser.add_argument("--quit-wait", type=float, default=30.0, help="seconds to wait for Warudo to exit")
    parser.add_argument("--timeout", type=float, default=60.0, help="seconds to wait for each reply")
    parser.add_argument("--delay", type=float, default=0.1, help="seconds to pause between commands")
    args = parser.parse_args()

    session = Session(args)

    for index, command in enumerate(args.commands):
        if index > 0:
            time.sleep(args.delay)

        if command.startswith("sleep:"):
            time.sleep(float(command.split(":", 1)[1]))
            continue

        if command.startswith("run:"):
            shell_command = command.split(":", 1)[1]
            print(f"> run {shell_command}")
            result = subprocess.run(shell_command, shell=True)
            if result.returncode != 0:
                raise SystemExit(f"error: '{shell_command}' exited with {result.returncode}")
            continue

        if command == "launch":
            session.launch()
            continue
        if command == "quit":
            session.quit()
            continue
        if command == "kill":
            session.kill()
            continue
        if command == "restart":
            session.quit()
            session.launch()
            continue

        if command.startswith("until:"):
            _, poll_command, expected = command.split(":", 2)
            print(f"> until '{poll_command}' answers '{expected}'")
            deadline = time.monotonic() + args.timeout
            while True:
                lines = session.connected().send_command(poll_command)
                if any(line.startswith(expected) for line in lines):
                    for line in lines:
                        print(line)
                    break
                if time.monotonic() >= deadline:
                    raise SystemExit(f"error: '{poll_command}' never answered '{expected}'")
                time.sleep(0.25)
            continue

        if command == "ready":
            command = "app info"

        print(f"> {command}")
        for line in session.connected().send_command(command):
            print(line)


if __name__ == "__main__":
    main()
