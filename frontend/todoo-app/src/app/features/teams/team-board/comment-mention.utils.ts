export const MENTION_TOKEN_REGEX = /@\{(\d+)\|([^}]+)\}/g;

export interface CommentBodySegment {
  kind: 'text' | 'mention';
  text: string;
  userId?: number;
  displayName?: string;
}

export interface DraftMention {
  start: number;
  end: number;
  userId: number;
  displayName: string;
}

export function buildMentionToken(userId: number, displayName: string): string {
  return `@{${userId}|${displayName}}`;
}

export function buildMentionText(displayName: string): string {
  return `@${displayName}`;
}

export function parseCommentBody(body: string): CommentBodySegment[] {
  if (!body) {
    return [];
  }

  const segments: CommentBodySegment[] = [];
  let lastIndex = 0;
  const regex = new RegExp(MENTION_TOKEN_REGEX.source, 'g');

  for (const match of body.matchAll(regex)) {
    const matchIndex = match.index ?? 0;

    if (matchIndex > lastIndex) {
      segments.push({ kind: 'text', text: body.slice(lastIndex, matchIndex) });
    }

    const userId = Number(match[1]);
    const displayName = match[2].trim();
    if (Number.isFinite(userId) && displayName) {
      segments.push({ kind: 'mention', text: displayName, userId, displayName });
    } else if (match[0]) {
      segments.push({ kind: 'text', text: match[0] });
    }

    lastIndex = matchIndex + match[0].length;
  }

  if (lastIndex < body.length) {
    segments.push({ kind: 'text', text: body.slice(lastIndex) });
  }

  return segments;
}

export function extractMentionedUserIds(body: string): number[] {
  const ids = new Set<number>();
  const regex = new RegExp(MENTION_TOKEN_REGEX.source, 'g');

  for (const match of body.matchAll(regex)) {
    const userId = Number(match[1]);
    if (Number.isFinite(userId)) {
      ids.add(userId);
    }
  }

  return [...ids];
}

export function syncDraftMentions(previousBody: string, nextBody: string, mentions: DraftMention[]): DraftMention[] {
  const prefixLength = longestCommonPrefixLength(previousBody, nextBody);
  const suffixLength = longestCommonSuffixLength(previousBody, nextBody, prefixLength);
  const previousEditEnd = previousBody.length - suffixLength;
  const nextEditEnd = nextBody.length - suffixLength;
  const delta = nextBody.length - previousBody.length;

  return mentions
    .flatMap((mention) => {
      if (mention.end <= prefixLength) {
        return [mention];
      }

      if (mention.start >= previousEditEnd) {
        return [
          {
            ...mention,
            start: mention.start + delta,
            end: mention.end + delta
          }
        ];
      }

      if (mention.start < previousEditEnd && mention.end > prefixLength) {
        return [];
      }

      return [mention];
    })
    .filter((mention) => nextBody.slice(mention.start, mention.end) === buildMentionText(mention.displayName));
}

export function encodeCommentBody(body: string, mentions: DraftMention[]): string {
  if (!body || mentions.length === 0) {
    return body;
  }

  let encoded = body;
  const sorted = [...mentions].sort((a, b) => b.start - a.start);

  for (const mention of sorted) {
    const visibleText = buildMentionText(mention.displayName);
    if (encoded.slice(mention.start, mention.end) !== visibleText) {
      continue;
    }

    encoded =
      encoded.slice(0, mention.start) +
      buildMentionToken(mention.userId, mention.displayName) +
      encoded.slice(mention.end);
  }

  return encoded;
}

function longestCommonPrefixLength(left: string, right: string): number {
  const max = Math.min(left.length, right.length);
  let index = 0;

  while (index < max && left[index] === right[index]) {
    index += 1;
  }

  return index;
}

function longestCommonSuffixLength(left: string, right: string, prefixLength: number): number {
  const leftRemaining = left.length - prefixLength;
  const rightRemaining = right.length - prefixLength;
  const max = Math.min(leftRemaining, rightRemaining);
  let index = 0;

  while (
    index < max &&
    left[left.length - 1 - index] === right[right.length - 1 - index]
  ) {
    index += 1;
  }

  return index;
}
