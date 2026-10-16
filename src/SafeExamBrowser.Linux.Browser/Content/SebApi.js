/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 * Ported from Safe Exam Browser (seb-win-refactoring) for the Linux port.
 *
 * The browser exam key and configuration key are inserted per frame URL, i.e. the
 * values are the hash values calculated for the URL of the current frame (see
 * KeyGenerator.CalculateBrowserExamKeyHash / CalculateConfigurationKeyHash).
 */

SafeExamBrowser = {
    version: 'SEB_Linux_%%_VERSION_%%',
    security: {
        browserExamKey: '%%_BEK_%%',
        configKey: '%%_CK_%%',
        updateKeys: (callback) => callback()
    }
}
