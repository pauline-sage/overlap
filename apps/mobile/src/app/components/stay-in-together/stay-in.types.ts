export type StayInActivity='menu'|'watch'|'read'|'quiz'|'cooking'|'drawing';
export interface StayInRequest {activity:StayInActivity;entryId?:number;contestId?:number;partnerPicks?:boolean;}
