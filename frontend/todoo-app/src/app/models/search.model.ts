export interface GlobalSearchTeam {
  id: number;
  name: string;
}

export interface GlobalSearchTask {
  id: number;
  title: string;
  teamId: number;
  teamName: string;
  boardColumnTitle: string;
}

export interface GlobalSearchPerson {
  id: number;
  email: string;
  displayName: string;
  hasProfilePhoto: boolean;
}

export interface GlobalSearchResult {
  teams: GlobalSearchTeam[];
  tasks: GlobalSearchTask[];
  people: GlobalSearchPerson[];
}
