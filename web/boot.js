// Copyright 2026 Tanbin (magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.
// Opens the first tool. Loaded last, after every tool file has registered itself in TOOL_NAMES / toolInit.
"use strict";
(function(){let t=location.hash.slice(1);if(!TOOL_NAMES[t]){try{t=localStorage.getItem('tool')||'foldersize'}catch(e){t='foldersize'}}showTool(t)})();
