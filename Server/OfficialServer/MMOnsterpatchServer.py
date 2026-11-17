#!/usr/bin/env python3
"""MMOnsterpatch Official Server visible-module bootstrap.

This YOLO repack build uses the current working v0.181 server modules from
server_modules/ instead of hiding them inside a base64 bundled bootstrap. That
makes the server easier to inspect, patch, and migrate without losing the
current social/bank databases.
"""
import os
import runpy
import sys


def _script_dir():
    try:
        return os.path.dirname(os.path.abspath(__file__))
    except Exception:
        return os.getcwd()


def _ensure_default_args(root):
    has_config = any(a == '--config' or a.startswith('--config=') for a in sys.argv[1:])
    if not has_config:
        sys.argv.extend(['--config', os.path.join(root, 'configs', 'worldserver.ini')])


def _main():
    root = _script_dir()
    os.chdir(root)
    for folder in ('data', 'logs', 'backups'):
        os.makedirs(os.path.join(root, folder), exist_ok=True)
    modules = os.path.join(root, 'server_modules')
    if modules not in sys.path:
        sys.path.insert(0, modules)
    _ensure_default_args(root)
    runpy.run_module('mmonsterpatch_server', run_name='__main__')


if __name__ == '__main__':
    _main()
