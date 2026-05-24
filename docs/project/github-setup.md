# GitHub Setup Plan

This repository was bootstrapped locally. Once the public personal repository exists, run the GitHub setup from the `main` branch.

## Branch Protection

Recommended `main` rules:

- Require pull request before merge.
- Require one approving review.
- Require status checks to pass.
- Require conversation resolution.
- Require linear history.
- Block force pushes.
- Block deletion.

## Milestones

Create:

- M0 Repo Bootstrap
- M1 Core Workbook Model
- M2 Lark Provider
- M3 Unity Package
- M4 Merge Review And Gate
- M5 Docs And Release

## Initial Issues

- M0: bootstrap repository structure and CI
- M1: harden semantic workbook import and validator
- M2: validate lark-cli export/read against disposable sheets
- M3: add Unity edit-mode package tests
- M4: add PR-friendly merge/gate annotations
- M5: prepare v0.1.0 release notes and package artifacts

## PR Review Rhythm

After opening a PR, wait 4-6 minutes for automated review. Fix actionable findings before merging. After merge, update the issue, milestone, and release notes.
