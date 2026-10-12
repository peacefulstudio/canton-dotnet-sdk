#!/usr/bin/env bash
# Copyright 2026 Peaceful Studio OÜ
# SPDX-License-Identifier: Apache-2.0

exec "$(dirname "${BASH_SOURCE[0]}")/regenerate.sh" rebuild-dar "$@"
