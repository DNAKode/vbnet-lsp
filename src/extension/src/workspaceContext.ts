/*---------------------------------------------------------------------------------------------
 *  VB.NET Language Support
 *  Licensed under the MIT License. See LICENSE in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

import * as path from 'path';

export type WorkspaceContextKind =
    | 'unknown'
    | 'solution'
    | 'singleProject'
    | 'allProjects'
    | 'selectContext'
    | 'empty';

export interface WorkspaceContext {
    kind: WorkspaceContextKind;
    workspaceRoot?: string;
    solutionPath?: string;
    projectPaths?: string[];
    solutionCandidates?: string[];
}

export const UnknownWorkspaceContext: WorkspaceContext = { kind: 'unknown' };

export function formatWorkspaceContextLabel(context: WorkspaceContext): string {
    switch (context.kind) {
        case 'solution':
            return context.solutionPath
                ? path.basename(context.solutionPath)
                : 'Solution';
        case 'singleProject':
            return context.projectPaths && context.projectPaths.length > 0
                ? path.basename(context.projectPaths[0])
                : 'Project';
        case 'allProjects': {
            const count = context.projectPaths?.length ?? 0;
            if (count === 1) {
                return context.projectPaths?.[0]
                    ? path.basename(context.projectPaths[0])
                    : '1 project';
            }
            return count > 0 ? `${count} projects` : 'Workspace Dev Mode';
        }
        case 'selectContext':
            return 'Select Context';
        case 'empty':
            return 'No VB.NET workspace';
        case 'unknown':
        default:
            return 'Workspace';
    }
}

export function formatWorkspaceContextDetail(context: WorkspaceContext): string {
    switch (context.kind) {
        case 'solution':
            return context.solutionPath
                ? `Solution: ${context.solutionPath}`
                : 'Solution mode';
        case 'singleProject':
            return context.projectPaths && context.projectPaths.length > 0
                ? `Project: ${context.projectPaths[0]}`
                : 'Single project mode';
        case 'allProjects': {
            const count = context.projectPaths?.length ?? 0;
            return count > 0
                ? `Workspace Dev Mode: ${count} project(s)`
                : 'Workspace Dev Mode';
        }
        case 'selectContext': {
            const solutionCount = context.solutionCandidates?.length ?? 0;
            const projectCount = context.projectPaths?.length ?? 0;
            const parts = [];
            if (solutionCount > 0) {
                parts.push(`${solutionCount} solution candidate(s)`);
            }
            if (projectCount > 0) {
                parts.push(`${projectCount} project(s) available`);
            }
            return parts.length > 0
                ? `Select VB.NET context: ${parts.join(', ')}`
                : 'Select VB.NET context';
        }
        case 'empty':
            return 'No .sln, .slnf, .slnx, or .vbproj files were found.';
        case 'unknown':
        default:
            return 'Workspace context has not been discovered yet.';
    }
}
