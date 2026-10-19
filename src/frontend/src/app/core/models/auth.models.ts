export interface LoginRequest {
  username: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  username: string;
  email: string;
  role: string;
  permissions: string[];
  expiresAt: string;
}

export interface AuthUser {
  username: string;
  email: string;
  role: string;
  permissions: string[];
  token: string;
  expiresAt: Date;
}
