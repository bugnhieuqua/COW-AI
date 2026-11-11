#!/usr/bin/env node
/**
 * COW.AI Unified E2E Test Suite Runner
 * Executes Tiers 1 through 4 using Node.js built-in test runner
 * and reports an aggregated pass/fail summary.
 */

import { spawn } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const PROJECT_ROOT = path.resolve(__dirname, '..');

const BASE_URL = process.env.COW_URL || 'http://127.0.0.1:5223';

console.log('================================================================');
console.log('  COW.AI HOMEPAGE REFACTORING - E2E TEST SUITE RUNNER (TIERS 1-4)');
console.log('================================================================');
console.log(`- Project Root: ${PROJECT_ROOT}`);
console.log(`- Target Server: ${BASE_URL}`);
console.log(`- Node Version: ${process.version}`);
console.log('----------------------------------------------------------------\n');

const testSuites = [
    {
        tier: 'Tier 1',
        title: 'Feature Coverage (10 Feature Areas)',
        file: path.join(__dirname, 'tier1-feature-coverage.test.mjs')
    },
    {
        tier: 'Tier 2',
        title: 'Boundary & Corner Cases',
        file: path.join(__dirname, 'tier2-boundary-corner.test.mjs')
    },
    {
        tier: 'Tier 3',
        title: 'Cross-Feature Combinations',
        file: path.join(__dirname, 'tier3-cross-combinations.test.mjs')
    },
    {
        tier: 'Tier 4',
        title: 'Real-World Scenarios',
        file: path.join(__dirname, 'tier4-real-world-scenarios.test.mjs')
    },
    {
        tier: 'Tier 5A',
        title: 'White-Box Adversarial Verification',
        file: path.join(__dirname, 'tier5-adversarial-verification.test.mjs')
    },
    {
        tier: 'Tier 5B',
        title: 'Adversarial Stress & Vulnerability Probes',
        file: path.join(__dirname, 'tier5-adversarial-stress.test.mjs')
    },
    {
        tier: 'Tier 6',
        title: 'New Models, Compact UI & Security Error Handling',
        file: path.join(__dirname, 'tier6-new-features-verification.test.mjs')
    },
    {
        tier: 'Tier 7',
        title: 'Reviewer Adversarial Probes & Deep Security Verification',
        file: path.join(__dirname, 'tier7-reviewer-adversarial-probes.test.mjs')
    },
    {
        tier: 'Tier 8',
        title: 'Reviewer R2 Deep Adversarial & Edge Case Probes',
        file: path.join(__dirname, 'tier8-reviewer-r2-deep-verification.test.mjs')
    },
    {
        tier: 'Tier 9',
        title: 'Reviewer R3 Deep Adversarial & Resiliency Probes',
        file: path.join(__dirname, 'tier9-reviewer-r3-deep-verification.test.mjs')
    },
    {
        tier: 'Tier 10',
        title: 'Section 20 - Pollinations AI Vietnamese Prompt & Image Containment',
        file: path.join(__dirname, 'tier10-pollinations-update.test.mjs')
    },
    {
        tier: 'Tier 11',
        title: 'Ollama Cloud & Free Models Integration Verification',
        file: path.join(__dirname, 'tier11-ollama-cloud-integration.test.mjs')
    }
];

// Pre-flight server health check
async function preflightCheck() {
    try {
        const res = await fetch(`${BASE_URL}/`, { signal: AbortSignal.timeout(3000) });
        if (res.ok) {
            console.log(`[PRE-FLIGHT] OK: Live server reachable at ${BASE_URL} (Status: ${res.status})\n`);
            return true;
        } else {
            console.warn(`[PRE-FLIGHT] WARNING: Server returned HTTP ${res.status}\n`);
            return false;
        }
    } catch (err) {
        console.warn(`[PRE-FLIGHT] WARNING: Live server not reachable (${err.message}). Tests using live HTTP will fail.\n`);
        return false;
    }
}

function runSuite(suite) {
    return new Promise((resolve) => {
        const startTime = Date.now();
        console.log(`>>> Running [${suite.tier}]: ${suite.title}`);

        const child = spawn(process.execPath, ['--test', suite.file], {
            cwd: PROJECT_ROOT,
            env: { ...process.env, COW_URL: BASE_URL },
            stdio: ['ignore', 'pipe', 'pipe']
        });

        let stdout = '';
        let stderr = '';

        child.stdout.on('data', (d) => {
            stdout += d.toString();
            process.stdout.write(d);
        });

        child.stderr.on('data', (d) => {
            stderr += d.toString();
            process.stderr.write(d);
        });

        child.on('close', (code) => {
            const duration = Date.now() - startTime;
            
            // Extract statistics from node:test output
            const passMatch = stdout.match(/ℹ pass (\d+)/);
            const failMatch = stdout.match(/ℹ fail (\d+)/);
            const totalMatch = stdout.match(/ℹ tests (\d+)/);

            const passed = passMatch ? parseInt(passMatch[1], 10) : (code === 0 ? 1 : 0);
            const failed = failMatch ? parseInt(failMatch[1], 10) : (code !== 0 ? 1 : 0);
            const total = totalMatch ? parseInt(totalMatch[1], 10) : (passed + failed);

            resolve({
                tier: suite.tier,
                title: suite.title,
                code,
                passed,
                failed,
                total,
                duration
            });
        });
    });
}

async function main() {
    await preflightCheck();

    const results = [];
    for (const suite of testSuites) {
        const res = await runSuite(suite);
        results.push(res);
        console.log(`<<< [${res.tier}] Finished in ${res.duration}ms (Passed: ${res.passed}, Failed: ${res.failed})\n`);
    }

    console.log('================================================================');
    console.log('  E2E TEST EXECUTION SUMMARY REPORT                             ');
    console.log('================================================================');
    console.log(
        'Tier'.padEnd(10) +
        'Scope'.padEnd(35) +
        'Passed'.padEnd(10) +
        'Failed'.padEnd(10) +
        'Duration'
    );
    console.log('-'.repeat(75));

    let grandTotal = 0;
    let grandPassed = 0;
    let grandFailed = 0;
    let totalDuration = 0;

    for (const r of results) {
        grandTotal += r.total;
        grandPassed += r.passed;
        grandFailed += r.failed;
        totalDuration += r.duration;

        console.log(
            r.tier.padEnd(10) +
            r.title.slice(0, 33).padEnd(35) +
            String(r.passed).padEnd(10) +
            String(r.failed).padEnd(10) +
            `${r.duration}ms`
        );
    }

    console.log('-'.repeat(75));
    console.log(
        'TOTAL'.padEnd(45) +
        String(grandPassed).padEnd(10) +
        String(grandFailed).padEnd(10) +
        `${totalDuration}ms`
    );
    console.log('================================================================');

    if (grandFailed === 0) {
        console.log(`\n🎉 SUCCESS: All ${grandTotal} E2E tests across 4 Tiers PASSED with 100% success rate!`);
        process.exit(0);
    } else {
        console.error(`\n❌ FAILURE: ${grandFailed} out of ${grandTotal} tests failed.`);
        process.exit(1);
    }
}

main().catch((err) => {
    console.error('Fatal error running tests:', err);
    process.exit(1);
});
