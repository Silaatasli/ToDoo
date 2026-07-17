export interface AuthResult {
  success: boolean;
  message: string;
  token?: string;
  refreshToken?: string;
  userId?: number;
  email?: string;
}

export interface AuthUser {
  userId: number;
}
