# Civica API Specification

## 🎯 Overview

Complete REST API specification for the Civica backend, designed to seamlessly replace Angular mock services. This API follows RESTful principles and provides all endpoints needed for the frontend application.

**Base URL**: `https://civica-api.railway.app/api` (production) or `http://localhost:5000/api` (development)

## 🔐 Authentication

All authenticated endpoints require a valid Supabase JWT token in the Authorization header:

```
Authorization: Bearer <supabase-jwt-token>
```

**Token Structure**: Standard Supabase JWT with user ID in the `sub` claim.

**Token Validation**: Server validates tokens against Supabase Auth service.

## 📚 API Endpoints

### 🔑 Authentication Endpoints

#### GET /api/auth/profile
Get current user profile information.

**Authentication**: Required
**Method**: `GET`
**URL**: `/api/auth/profile`

**Response 200 (Success)**:
```json
{
  "id": "uuid",
  "email": "user@example.com",
  "displayName": "Ion Popescu",
  "photoUrl": "https://example.com/photo.jpg",
  "county": "București",
  "city": "București", 
  "district": "Sector 5",
  "residenceType": "apartment",
  "points": 150,
  "level": 2,
  "createdAt": "2024-01-15T10:30:00Z",
  "emailVerified": true
}
```

**Response 404 (User not found)**:
```json
{
  "error": "User profile not found"
}
```

---

#### POST /api/auth/profile
Create user profile after Supabase registration.

**Authentication**: Required
**Method**: `POST`
**URL**: `/api/auth/profile`

**Request Body**:
```json
{
  "displayName": "Ion Popescu",
  "photoUrl": "https://example.com/photo.jpg",
  "county": "București",
  "city": "București",
  "district": "Sector 5",
  "residenceType": "apartment"
}
```

**Response 201 (Created)**:
```json
{
  "id": "uuid",
  "email": "user@example.com",
  "displayName": "Ion Popescu",
  "photoUrl": "https://example.com/photo.jpg",
  "county": "București",
  "city": "București",
  "district": "Sector 5",
  "residenceType": "apartment",
  "points": 0,
  "level": 1,
  "createdAt": "2024-01-15T10:30:00Z",
  "emailVerified": true
}
```

**Response 400 (Validation Error)**:
```json
{
  "error": "Display name is required",
  "details": {
    "displayName": ["Display name must be between 2 and 100 characters"]
  }
}
```

---

#### PUT /api/auth/profile
Update user profile information.

**Authentication**: Required
**Method**: `PUT`
**URL**: `/api/auth/profile`

**Request Body** (partial updates supported):
```json
{
  "displayName": "Ion Popescu",
  "photoUrl": "https://example.com/new-photo.jpg",
  "residenceType": "house",
  "issueUpdatesEnabled": true,
  "communityNewsEnabled": false,
  "monthlyDigestEnabled": true,
  "achievementsEnabled": true
}
```

**Response 200 (Updated)**:
```json
{
  "id": "uuid",
  "email": "user@example.com",
  "displayName": "Ion Popescu",
  "photoUrl": "https://example.com/new-photo.jpg",
  "county": "București",
  "city": "București",
  "district": "Sector 5", 
  "residenceType": "house",
  "points": 150,
  "level": 2,
  "createdAt": "2024-01-15T10:30:00Z",
  "emailVerified": true
}
```

### 📋 Issues Endpoints

#### GET /api/issues
Get paginated list of approved public issues.

**Authentication**: Optional
**Method**: `GET`
**URL**: `/api/issues`

**Query Parameters**:
- `page` (integer, default: 1): Page number
- `pageSize` (integer, default: 12, max: 50): Items per page
- `category` (string): Filter by category (infrastructure, environment, transportation, public_services, safety, other)
- `urgency` (string): Filter by urgency (low, medium, high, urgent)
- `district` (string): Filter by district
- `sortBy` (string, default: "date"): Sort by (date, emails, urgency)
- `sortDescending` (boolean, default: true): Sort direction

**Example**: `/api/issues?page=1&pageSize=12&category=infrastructure&sortBy=emails&sortDescending=true`

**Response 200 (Success)**:
```json
{
  "items": [
    {
      "id": "uuid",
      "title": "Trotuar deteriorat pe Strada Libertății",
      "description": "Trotuarul este grav deteriorat cu gropi mari...",
      "category": "infrastructure",
      "address": "Strada Libertății nr. 45, Sector 5, București",
      "urgency": "high",
      "emailsSent": 47,
      "createdAt": "2024-01-15T10:30:00Z",
      "mainPhotoUrl": "https://example.com/photo.jpg"
    }
  ],
  "totalItems": 150,
  "page": 1,
  "pageSize": 12,
  "totalPages": 13
}
```

**Response 400 (Invalid parameters)**:
```json
{
  "error": "Invalid page size. Maximum allowed is 50"
}
```

---

#### GET /api/issues/{id}
Get detailed information about a specific issue.

**Authentication**: Optional
**Method**: `GET`
**URL**: `/api/issues/{id}`

**Path Parameters**:
- `id` (uuid): Issue ID

**Response 200 (Success)**:
```json
{
  "id": "uuid",
  "title": "Trotuar deteriorat pe Strada Libertății",
  "description": "Trotuarul este grav deteriorat cu gropi mari care pun în pericol siguranța pietonilor. Problema există de mai multe săptămâni și se agravează cu fiecare zi.",
  "category": "infrastructure",
  "address": "Strada Libertății nr. 45, Sector 5, București",
  "latitude": 44.4165,
  "longitude": 26.0765,
  "urgency": "high",
  "emailsSent": 47,
  "currentSituation": "Trotuarul prezintă multiple gropi și fisuri care fac deplasarea periculoasă.",
  "desiredOutcome": "Refacerea completă a trotuarului pe o lungime de 100m.",
  "communityImpact": "Peste 500 de locuitori folosesc zilnic acest trotuar pentru acces la școală și magazine.",
  "createdAt": "2024-01-15T10:30:00Z",
  "photos": [
    {
      "id": "uuid",
      "url": "https://example.com/photo1.jpg",
      "thumbnailUrl": "https://example.com/photo1-thumb.jpg",
      "caption": "Groapă principală"
    },
    {
      "id": "uuid",
      "url": "https://example.com/photo2.jpg",
      "thumbnailUrl": "https://example.com/photo2-thumb.jpg",
      "caption": "Vedere generală"
    }
  ],
  "user": {
    "id": "uuid",
    "displayName": "Ion Popescu"
  }
}
```

**Response 404 (Not found)**:
```json
{
  "error": "Issue not found"
}
```

---

#### POST /api/issues
Create a new issue (requires authentication).

**Authentication**: Required
**Method**: `POST`
**URL**: `/api/issues`

**Request Body**:
```json
{
  "title": "Semaforul defect la intersecția Eroilor - Păcii",
  "description": "Semaforul nu funcționează de 3 zile, creând probleme grave de trafic și siguranță.",
  "category": "transportation",
  "address": "Intersecția Eroilor cu Păcii, Sector 5, București",
  "latitude": 44.4142,
  "longitude": 26.0798,
  "locationAccuracy": 10,
  "neighborhood": "Centrul Civic",
  "landmark": "Lângă Parcul Eroilor",
  "urgency": "urgent",
  "currentSituation": "Intersecția este complet necontrolată, cu risc major de accidente.",
  "desiredOutcome": "Repararea imediată a semaforului sau instalarea unui polițist pentru dirijarea traficului.",
  "communityImpact": "Această intersecție este folosită zilnic de peste 2000 de vehicule și 500 de pietoni.",
  "aiGeneratedDescription": "AI enhanced description...",
  "aiProposedSolution": "AI suggested solution...",
  "aiConfidence": 0.89,
  "photos": [
    {
      "url": "https://example.com/uploaded-photo1.jpg",
      "thumbnailUrl": "https://example.com/uploaded-photo1-thumb.jpg",
      "caption": "Semafor defect",
      "quality": "high"
    }
  ]
}
```

**Response 201 (Created)**:
```json
{
  "id": "uuid",
  "status": "submitted",
  "createdAt": "2024-01-15T10:30:00Z"
}
```

**Response 400 (Validation Error)**:
```json
{
  "error": "Validation failed",
  "details": {
    "title": ["Title is required"],
    "description": ["Description must be at least 50 characters"],
    "photos": ["At least one photo is required"]
  }
}
```

---

#### PUT /api/issues/{id}/email-sent
Track that user sent an email for this issue.

**Authentication**: Required
**Method**: `PUT`
**URL**: `/api/issues/{id}/email-sent`

**Path Parameters**:
- `id` (uuid): Issue ID

**Request Body**:
```json
{
  "authorityEmail": "contact@primarie5.ro",
  "authorityName": "Primăria Sector 5"
}
```

**Response 200 (Success)**:
```json
{
  "success": true,
  "message": "Email tracked successfully",
  "newEmailCount": 48
}
```

**Response 400 (Already tracked)**:
```json
{
  "success": false,
  "message": "Email already tracked for this issue"
}
```

**Response 404 (Issue not found)**:
```json
{
  "error": "Issue not found"
}
```

### 👤 User Management Endpoints

#### GET /api/user/gamification
Get user's gamification data (points, badges, achievements).

**Authentication**: Required
**Method**: `GET`
**URL**: `/api/user/gamification`

**Response 200 (Success)**:
```json
{
  "points": 150,
  "level": 2,
  "badges": [
    {
      "id": "uuid",
      "name": "Civic Starter",
      "description": "Reported your first community issue",
      "iconUrl": "/assets/badges/civic-starter.svg",
      "category": "starter",
      "rarity": "common",
      "earnedAt": "2024-01-15T10:30:00Z"
    },
    {
      "id": "uuid",
      "name": "Picture Perfect",
      "description": "Uploaded high-quality photos with your report",
      "iconUrl": "/assets/badges/picture-perfect.svg",
      "category": "progress",
      "rarity": "uncommon", 
      "earnedAt": "2024-01-20T14:15:00Z"
    }
  ],
  "stats": {
    "issuesReported": 4,
    "issuesResolved": 2,
    "communityVotes": 23,
    "commentsGiven": 8,
    "helpfulComments": 6,
    "qualityScore": 87.5,
    "approvalRate": 85.0
  },
  "achievements": [
    {
      "id": "uuid",
      "title": "First Steps",
      "description": "Report your first issue",
      "progress": 1,
      "maxProgress": 1,
      "completed": true,
      "completedAt": "2024-01-15T10:30:00Z",
      "reward": {
        "points": 50,
        "badge": "civic-starter"
      }
    },
    {
      "id": "uuid",
      "title": "Community Champion",
      "description": "Report 10 issues",
      "progress": 4,
      "maxProgress": 10,
      "completed": false,
      "reward": {
        "points": 200,
        "badge": "community-champion"
      }
    }
  ],
  "streaks": {
    "currentLoginStreak": 3,
    "longestLoginStreak": 7,
    "currentVotingStreak": 2,
    "longestVotingStreak": 5,
    "lastActivityDate": "2024-01-25T18:00:00Z"
  },
  "leaderboardPosition": {
    "overall": 847,
    "monthly": 156,
    "category": 23,
    "neighborhood": 8,
    "totalUsers": 1250
  }
}
```

**Response 404 (User not found)**:
```json
{
  "error": "User gamification data not found"
}
```

---

#### GET /api/user/issues
Get user's own issues (all statuses).

**Authentication**: Required
**Method**: `GET`
**URL**: `/api/user/issues`

**Query Parameters**:
- `page` (integer, default: 1): Page number
- `pageSize` (integer, default: 10): Items per page
- `status` (string): Filter by status (draft, submitted, under_review, approved, rejected)

**Response 200 (Success)**:
```json
{
  "items": [
    {
      "id": "uuid",
      "title": "My Issue Title",
      "description": "Issue description...",
      "category": "infrastructure",
      "status": "approved",
      "urgency": "high",
      "emailsSent": 12,
      "createdAt": "2024-01-15T10:30:00Z",
      "updatedAt": "2024-01-16T09:15:00Z",
      "adminNotes": "Approved for public visibility",
      "mainPhotoUrl": "https://example.com/photo.jpg"
    }
  ],
  "totalItems": 4,
  "page": 1,
  "pageSize": 10,
  "totalPages": 1
}
```

---

#### PUT /api/user/points
Award points to user (internal endpoint, used by gamification system).

**Authentication**: Required (Service-to-Service)
**Method**: `PUT`
**URL**: `/api/user/points`

**Request Body**:
```json
{
  "points": 50,
  "reason": "Issue approved"
}
```

**Response 200 (Success)**:
```json
{
  "newPoints": 50,
  "totalPoints": 200,
  "newLevel": 2,
  "leveledUp": false
}
```

### 🛡️ Admin Endpoints

All admin endpoints require admin role in JWT token.

#### GET /api/admin/pending-issues
Get issues pending admin review.

**Authentication**: Required (Admin)
**Method**: `GET`
**URL**: `/api/admin/pending-issues`

**Query Parameters**:
- `page` (integer, default: 1): Page number
- `pageSize` (integer, default: 20): Items per page
- `category` (string): Filter by category
- `urgency` (string): Filter by urgency
- `sortBy` (string, default: "date"): Sort by (date, urgency)

**Response 200 (Success)**:
```json
{
  "items": [
    {
      "id": "uuid",
      "title": "Broken Street Light on Strada Libertății",
      "description": "The street light has been non-functional...",
      "category": "infrastructure",
      "address": "Strada Libertății nr. 23, Sector 5, București",
      "latitude": 44.4268,
      "longitude": 26.1025,
      "urgency": "high",
      "status": "submitted",
      "submittedAt": "2024-01-20T10:30:00Z",
      "priority": "high",
      "publicVisibility": true,
      "user": {
        "id": "uuid",
        "displayName": "Ion Popescu",
        "email": "ion.popescu@example.com"
      },
      "photos": [
        {
          "id": "uuid", 
          "url": "https://example.com/photo.jpg",
          "thumbnailUrl": "https://example.com/photo-thumb.jpg",
          "quality": "high"
        }
      ],
      "aiAnalysis": {
        "description": "AI enhanced description",
        "proposedSolution": "Municipal lighting team should...",
        "confidence": 0.89
      }
    }
  ],
  "totalItems": 15,
  "page": 1,
  "pageSize": 20,
  "totalPages": 1
}
```

---

#### GET /api/admin/issues/{id}
Get detailed issue information for admin review.

**Authentication**: Required (Admin)
**Method**: `GET`
**URL**: `/api/admin/issues/{id}`

**Response 200 (Success)**:
```json
{
  "id": "uuid",
  "title": "Broken Street Light on Strada Libertății",
  "description": "The street light has been non-functional for over a week, creating safety concerns for pedestrians walking at night.",
  "category": "infrastructure",
  "address": "Strada Libertății nr. 23, Sector 5, București",
  "latitude": 44.4268,
  "longitude": 26.1025,
  "urgency": "high",
  "status": "submitted",
  "submittedAt": "2024-01-20T10:30:00Z",
  "priority": "high",
  "publicVisibility": true,
  "currentSituation": "Street light completely non-functional",
  "desiredOutcome": "Repair or replace the street light",
  "communityImpact": "Safety concerns for evening pedestrians",
  "aiGeneratedText": {
    "description": "AI enhanced description",
    "proposedSolution": "Municipal lighting team should replace...",
    "confidence": 0.89
  },
  "user": {
    "id": "uuid",
    "displayName": "Ion Popescu",
    "email": "ion.popescu@example.com",
    "phone": "+40123456789"
  },
  "photos": [
    {
      "id": "uuid",
      "url": "https://example.com/photo1.jpg",
      "thumbnailUrl": "https://example.com/photo1-thumb.jpg",
      "caption": "Broken street light",
      "quality": "high",
      "metadata": {
        "size": 1024000,
        "dimensions": { "width": 1920, "height": 1080 },
        "format": "image/jpeg"
      }
    }
  ]
}
```

---

#### PUT /api/admin/issues/{id}/approve
Approve an issue for public visibility.

**Authentication**: Required (Admin)
**Method**: `PUT`
**URL**: `/api/admin/issues/{id}/approve`

**Request Body**:
```json
{
  "notes": "Issue verified and approved for public visibility",
  "priority": "high",
  "assignedDepartment": "Infrastructure & Public Works",
  "estimatedResolutionTime": "2-4 weeks",
  "publicVisibility": true
}
```

**Response 200 (Success)**:
```json
{
  "success": true,
  "issue": {
    "id": "uuid",
    "status": "approved",
    "reviewedAt": "2024-01-21T14:30:00Z",
    "reviewedBy": "admin@civica.ro",
    "adminNotes": "Issue verified and approved for public visibility",
    "priority": "high",
    "assignedDepartment": "Infrastructure & Public Works",
    "estimatedResolutionTime": "2-4 weeks",
    "publicVisibility": true
  }
}
```

---

#### PUT /api/admin/issues/{id}/reject
Reject an issue with reason.

**Authentication**: Required (Admin)
**Method**: `PUT`
**URL**: `/api/admin/issues/{id}/reject`

**Request Body**:
```json
{
  "reason": "Insufficient information provided. Please add more photos and details about the exact location.",
  "notes": "Requesting additional documentation from user"
}
```

**Response 200 (Success)**:
```json
{
  "success": true,
  "issue": {
    "id": "uuid",
    "status": "rejected",
    "reviewedAt": "2024-01-21T14:30:00Z",
    "reviewedBy": "admin@civica.ro",
    "rejectionReason": "Insufficient information provided. Please add more photos and details about the exact location.",
    "adminNotes": "Requesting additional documentation from user"
  }
}
```

---

#### PUT /api/admin/issues/{id}/request-changes
Request changes to an issue.

**Authentication**: Required (Admin)
**Method**: `PUT`
**URL**: `/api/admin/issues/{id}/request-changes`

**Request Body**:
```json
{
  "notes": "Please provide more specific location details and additional photos showing the full extent of the problem.",
  "requestedChanges": [
    "Add more photos from different angles",
    "Provide exact street address or nearby landmarks",
    "Clarify the scope of the problem"
  ]
}
```

**Response 200 (Success)**:
```json
{
  "success": true,
  "issue": {
    "id": "uuid", 
    "status": "under_review",
    "reviewedAt": "2024-01-21T14:30:00Z",
    "reviewedBy": "admin@civica.ro",
    "adminNotes": "Please provide more specific location details and additional photos showing the full extent of the problem."
  }
}
```

---

#### GET /api/admin/statistics
Get admin dashboard statistics.

**Authentication**: Required (Admin)
**Method**: `GET`
**URL**: `/api/admin/statistics`

**Query Parameters**:
- `period` (string, default: "30d"): Statistics period (7d, 30d, 90d, 1y)

**Response 200 (Success)**:
```json
{
  "totalSubmissions": 150,
  "pendingReview": 12,
  "approvedToday": 5,
  "rejectedToday": 2,
  "averageReviewTime": 18.5,
  "categoryBreakdown": {
    "Infrastructure": 45,
    "Environment": 32,
    "Transportation": 28,
    "Public Services": 20,
    "Safety": 15,
    "Other": 10
  },
  "urgencyBreakdown": {
    "urgent": 8,
    "high": 35,
    "medium": 82,
    "low": 25
  },
  "approvalRate": 78.5,
  "monthlyTrend": [
    { "month": "Jan", "submitted": 45, "approved": 38, "rejected": 7 },
    { "month": "Feb", "submitted": 52, "approved": 43, "rejected": 9 }
  ]
}
```

---

#### POST /api/admin/bulk-approve
Bulk approve multiple issues.

**Authentication**: Required (Admin)
**Method**: `POST`
**URL**: `/api/admin/bulk-approve`

**Request Body**:
```json
{
  "issueIds": [
    "uuid1",
    "uuid2", 
    "uuid3"
  ],
  "notes": "Batch approved after verification",
  "assignedDepartment": "Infrastructure & Public Works"
}
```

**Response 200 (Success)**:
```json
{
  "successCount": 3,
  "failureCount": 0,
  "results": [
    {
      "issueId": "uuid1",
      "success": true,
      "status": "approved"
    },
    {
      "issueId": "uuid2", 
      "success": true,
      "status": "approved"
    },
    {
      "issueId": "uuid3",
      "success": true,
      "status": "approved"
    }
  ]
}
```

### 🏆 Gamification Endpoints

#### GET /api/gamification/badges
Get all available badges.

**Authentication**: Optional
**Method**: `GET`
**URL**: `/api/gamification/badges`

**Response 200 (Success)**:
```json
{
  "badges": [
    {
      "id": "uuid",
      "name": "Civic Starter",
      "description": "Reported your first community issue",
      "iconUrl": "/assets/badges/civic-starter.svg",
      "category": "starter",
      "rarity": "common",
      "requirements": {
        "type": "issues_reported",
        "value": 1,
        "description": "Report your first issue"
      }
    },
    {
      "id": "uuid",
      "name": "Community Champion",
      "description": "Active community member with 100+ points",
      "iconUrl": "/assets/badges/community-champion.svg",
      "category": "achievement",
      "rarity": "rare",
      "requirements": {
        "type": "points",
        "value": 100,
        "description": "Earn 100 points"
      }
    }
  ]
}
```

---

#### GET /api/gamification/achievements
Get all available achievements.

**Authentication**: Optional  
**Method**: `GET`
**URL**: `/api/gamification/achievements`

**Response 200 (Success)**:
```json
{
  "achievements": [
    {
      "id": "uuid",
      "title": "First Steps",
      "description": "Report your first issue",
      "maxProgress": 1,
      "rewardPoints": 50,
      "rewardBadge": {
        "id": "uuid",
        "name": "Civic Starter"
      },
      "category": "issues"
    },
    {
      "id": "uuid",
      "title": "Community Voice",
      "description": "Vote on 50 community issues",
      "maxProgress": 50,
      "rewardPoints": 150,
      "rewardBadge": null,
      "category": "engagement"
    }
  ]
}
```

---

#### GET /api/gamification/leaderboard
Get user leaderboard.

**Authentication**: Optional
**Method**: `GET`
**URL**: `/api/gamification/leaderboard`

**Query Parameters**:
- `period` (string, default: "all"): Time period (all, monthly, weekly)
- `category` (string): Leaderboard category (points, issues_reported, community_votes)
- `limit` (integer, default: 50): Number of users to return

**Response 200 (Success)**:
```json
{
  "leaderboard": [
    {
      "rank": 1,
      "user": {
        "id": "uuid",
        "displayName": "Maria Ionescu"
      },
      "points": 2350,
      "level": 3,
      "issuesReported": 15,
      "issuesResolved": 8
    },
    {
      "rank": 2,
      "user": {
        "id": "uuid",
        "displayName": "Ion Popescu"
      },
      "points": 1950,
      "level": 2,
      "issuesReported": 12,
      "issuesResolved": 6
    }
  ],
  "userPosition": {
    "rank": 45,
    "totalUsers": 1250
  }
}
```

## 🔧 Utility Endpoints

#### GET /api/health
Health check endpoint.

**Authentication**: None
**Method**: `GET`
**URL**: `/api/health`

**Response 200 (Success)**:
```json
{
  "status": "Healthy",
  "timestamp": "2024-01-25T14:30:00Z",
  "version": "1.0.0",
  "database": "connected",
  "supabase": "connected"
}
```

---

#### GET /api/categories
Get issue categories.

**Authentication**: None
**Method**: `GET`  
**URL**: `/api/categories`

**Response 200 (Success)**:
```json
{
  "categories": [
    {
      "id": "infrastructure",
      "name": "Infrastructure", 
      "description": "Roads, sidewalks, utilities",
      "icon": "🚧",
      "examples": ["Potholes", "Broken sidewalks", "Street lighting", "Water pipes"]
    },
    {
      "id": "environment",
      "name": "Environment",
      "description": "Parks, pollution, cleanliness", 
      "icon": "🌳",
      "examples": ["Illegal dumping", "Park maintenance", "Air quality", "Noise pollution"]
    },
    {
      "id": "transportation",
      "name": "Transportation",
      "description": "Traffic, parking, public transport",
      "icon": "🚦", 
      "examples": ["Traffic signals", "Parking issues", "Bus stops", "Bike lanes"]
    },
    {
      "id": "public_services",
      "name": "Public Services",
      "description": "Government buildings, services",
      "icon": "🏢",
      "examples": ["Office hours", "Service quality", "Accessibility", "Staff issues"]
    },
    {
      "id": "safety", 
      "name": "Safety & Security",
      "description": "Crime, emergency services, safety hazards",
      "icon": "🛡️",
      "examples": ["Poor lighting", "Dangerous areas", "Emergency response", "Vandalism"]
    },
    {
      "id": "other",
      "name": "Other", 
      "description": "Other community issues",
      "icon": "📝",
      "examples": ["Community events", "Suggestions", "General concerns"]
    }
  ]
}
```

## 🚨 Error Handling

### Standard Error Response Format

All error responses follow this structure:

```json
{
  "error": "Human readable error message",
  "code": "ERROR_CODE",
  "timestamp": "2024-01-25T14:30:00Z",
  "path": "/api/issues",
  "details": {
    "field": ["Specific validation error"]
  }
}
```

### HTTP Status Codes

- **200 OK**: Request successful
- **201 Created**: Resource created successfully
- **400 Bad Request**: Invalid request data
- **401 Unauthorized**: Authentication required or invalid token
- **403 Forbidden**: Insufficient permissions
- **404 Not Found**: Resource not found
- **409 Conflict**: Resource already exists or conflict
- **422 Unprocessable Entity**: Validation errors
- **429 Too Many Requests**: Rate limit exceeded  
- **500 Internal Server Error**: Server error

### Common Error Codes

- `INVALID_TOKEN`: JWT token invalid or expired
- `USER_NOT_FOUND`: User profile not found
- `ISSUE_NOT_FOUND`: Issue not found
- `VALIDATION_ERROR`: Request validation failed
- `PERMISSION_DENIED`: Insufficient permissions
- `RATE_LIMIT_EXCEEDED`: Too many requests
- `EMAIL_ALREADY_TRACKED`: Email already tracked for this issue
- `PROFILE_ALREADY_EXISTS`: User profile already exists

## 🔐 Security Considerations

### Authentication
- All JWT tokens validated against Supabase
- Tokens must include valid user ID in `sub` claim
- Admin endpoints require admin role in token

### Rate Limiting
- Anonymous endpoints: 100 requests/hour per IP
- Authenticated endpoints: 1000 requests/hour per user  
- Admin endpoints: 10000 requests/hour per admin
- Email tracking: 10 requests/hour per user per issue

### Input Validation
- All inputs validated and sanitized
- File uploads limited to images, max 5MB per file
- Text fields have length limits to prevent abuse
- SQL injection prevention with parameterized queries

### Data Privacy
- Users can only access their own data
- Public issues show limited user information
- Admin access is logged and audited
- Row Level Security enforced at database level

## 📊 Performance Targets

- **Response Time**: <200ms average, <500ms P95
- **Availability**: 99.9% uptime
- **Throughput**: 1000 requests/minute per instance
- **Database**: <100ms query time for common operations

## 🔄 API Versioning

Current version: **v1**

All endpoints are prefixed with `/api` (implied v1).

Future versions will use `/api/v2`, `/api/v3`, etc.

**Breaking Changes**: Will increment major version
**New Features**: Added to current version with optional parameters
**Deprecation**: 6-month notice period for endpoint deprecation

## 📝 Request/Response Examples

### Create Issue with Authentication

**Request**:
```bash
curl -X POST https://civica-api.railway.app/api/issues \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..." \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Broken sidewalk on Main Street",
    "description": "Large crack in sidewalk causing safety hazard",
    "category": "infrastructure", 
    "address": "Main Street nr. 123",
    "latitude": 44.4268,
    "longitude": 26.1025,
    "urgency": "high",
    "photos": [{
      "url": "https://example.com/photo.jpg",
      "quality": "high"
    }]
  }'
```

**Response**:
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "status": "submitted",
  "createdAt": "2024-01-25T14:30:00Z"
}
```

### Track Email Sent

**Request**:
```bash
curl -X PUT https://civica-api.railway.app/api/issues/550e8400-e29b-41d4-a716-446655440000/email-sent \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..." \
  -H "Content-Type: application/json" \
  -d '{
    "authorityEmail": "contact@primarie5.ro",
    "authorityName": "Primăria Sector 5"
  }'
```

**Response**:
```json
{
  "success": true,
  "message": "Email tracked successfully",
  "newEmailCount": 48
}
```

## 🧪 Testing the API

### Using curl

```bash
# Get public issues
curl https://civica-api.railway.app/api/issues?page=1&pageSize=5

# Get issue details
curl https://civica-api.railway.app/api/issues/550e8400-e29b-41d4-a716-446655440000

# Health check
curl https://civica-api.railway.app/api/health
```

### Using Postman

Import the Postman collection: [Download Collection](docs/api/civica-postman-collection.json)

### Frontend Integration

Update Angular environment configuration:

```typescript
// environment.ts
export const environment = {
  production: false,
  apiUrl: 'http://localhost:5000/api',
  supabaseUrl: 'https://your-project.supabase.co',
  supabaseAnonKey: 'your-anon-key'
};

// environment.prod.ts
export const environment = {
  production: true,
  apiUrl: 'https://civica-api.railway.app/api',
  supabaseUrl: 'https://your-project.supabase.co',
  supabaseAnonKey: 'your-anon-key'
};
```

## 📚 Implementation Notes

1. **Pagination**: All list endpoints support pagination with consistent parameters
2. **Filtering**: Common filter parameters across similar endpoints
3. **Sorting**: Flexible sorting options with sensible defaults
4. **Validation**: Comprehensive input validation with detailed error messages
5. **Performance**: Optimized queries with appropriate indexes
6. **Security**: Row-level security and role-based access control
7. **Monitoring**: All endpoints include logging and metrics
8. **Documentation**: OpenAPI/Swagger documentation available at `/swagger`

## 🎯 Success Criteria

The API is successful when:
- ✅ All frontend mock services are successfully replaced
- ✅ Authentication works seamlessly with Supabase
- ✅ All CRUD operations work correctly
- ✅ Performance targets are met (<200ms average)
- ✅ Error handling provides meaningful feedback
- ✅ Admin workflows function properly
- ✅ Gamification system tracks correctly
- ✅ No regressions in frontend functionality

---

**Implementation Priority**: Implement endpoints in this order:
1. Authentication endpoints (/api/auth/*)
2. Public issues endpoints (GET /api/issues, GET /api/issues/{id})
3. Issue creation (POST /api/issues, PUT /api/issues/{id}/email-sent)
4. User gamification (GET /api/user/gamification)
5. Admin endpoints (/api/admin/*)
6. Utility endpoints (/api/health, /api/categories)