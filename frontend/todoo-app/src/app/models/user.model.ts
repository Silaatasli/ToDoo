export interface UserProfile {
  id: number;
  email: string;
  firstName?: string | null;
  lastName?: string | null;
  phoneNumber?: string | null;
  title?: string | null;
  createdDate: string;
  isSelf: boolean;
  hasProfilePhoto: boolean;
}

export interface UpdateProfileRequest {
  firstName?: string | null;
  lastName?: string | null;
  phoneNumber?: string | null;
  title?: string | null;
}

export interface UserSearchResult {
  id: number;
  email: string;
  firstName?: string | null;
  lastName?: string | null;
  displayName: string;
  hasProfilePhoto: boolean;
}
